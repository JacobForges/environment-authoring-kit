#!/usr/bin/env python3
"""Generic Q&A tab planner — one session + brief per Environment Kit wizard tab."""
from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable

import build_planner as bp
import wizard_checklist as wc
import wizard_session_public as wsp
import wizard_tab_research as wtr
from envkit_paths import atomic_write_text
from world_tab_brains import evaluate_tab_brain
import world_state_snapshot as wss

_CHECKLIST_RULES = """
## Checklist rules (critical)
- On EVERY turn, include `"checklist"` in your JSON with ALL items; update exactly one (or more if user covered them) to `done:true` with a concrete `decision` when the user answered.
- Add `"checklistAdditions": [{ "id", "label", "category" }]` when a new sub-topic needs its own row — session stays open until every row is done with implementable decisions.
- Set `done:true` only for items the user clearly addressed this turn.
- Never skip updating checklist — the pipeline depends on it.
- `assistantMessage` is user-facing markdown only — never paste raw JSON there.
"""


def _utc() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


@dataclass
class TabPlannerConfig:
    tab_id: str
    kind: str
    title: str
    session_rel: Path
    brief_rel: Path
    help_intro: str
    system_qna: str
    default_checklist: list[dict[str, Any]]
    presets: dict[str, dict[str, Any]] = field(default_factory=dict)
    topic_hints: dict[str, str] = field(default_factory=dict)
    brief_finalize_note: str = ""
    on_approve_brief: Callable[[dict[str, Any]], dict[str, Any]] | None = None


class TabPlanner:
    def __init__(self, config: TabPlannerConfig) -> None:
        self.config = config

    def _session_path(self, hub: Path) -> Path:
        return hub.expanduser().resolve() / self.config.session_rel

    def _brief_path(self, hub: Path) -> Path:
        return hub.expanduser().resolve() / self.config.brief_rel

    def _read_session(self, hub: Path) -> dict[str, Any] | None:
        p = self._session_path(hub)
        if not p.is_file():
            return None
        try:
            return json.loads(p.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            return None

    def _write_session(self, hub: Path, doc: dict[str, Any]) -> None:
        p = self._session_path(hub)
        p.parent.mkdir(parents=True, exist_ok=True)
        doc["tabId"] = self.config.tab_id
        doc["updatedUtc"] = _utc()
        atomic_write_text(p, json.dumps(doc, indent=2) + "\n")

    def _session_still_current(self, hub: Path, doc: dict[str, Any]) -> bool:
        current = self._read_session(hub)
        if not current:
            return False
        return str(current.get("createdUtc") or "") == str(doc.get("createdUtc") or "")

    def _build_qna_system(self, doc: dict[str, Any]) -> str:
        next_id = wc.next_pending_id(doc, self.config.default_checklist) or "none"
        injection = wtr.build_prompt_injection(self.config.tab_id, doc)
        world_state = {}
        try:
            hub_root = Path(os.environ.get("HUB_ROOT") or Path.cwd())
            world_state = wss.build_world_state(hub_root)
        except Exception:
            world_state = {}
        if world_state:
            doc["worldState"] = world_state
        world_block = wss.build_prompt_block(world_state) if world_state else ""
        return (
            self.config.system_qna
            + "\n\n"
            + injection
            + "\n\n"
            + world_block
            + _CHECKLIST_RULES
            + f"\n\nNext pending checklist id: {next_id}\n"
            + f"Current checklist: {json.dumps(wc.ensure_checklist_shape(doc, self.config.default_checklist))}\n"
        )

    def public_session(self, hub: Path) -> dict[str, Any]:
        doc = self._read_session(hub)
        if doc and bp._clear_stale_streaming(doc):
            self._write_session(hub, doc)
        return wsp.public_tab_session(
            doc,
            tab_id=self.config.tab_id,
            help_intro=self.config.help_intro,
            default_checklist=self.config.default_checklist,
            brief_rel=str(self.config.brief_rel),
        )

    def session_pulse(self, hub: Path) -> dict[str, Any]:
        doc = self._read_session(hub)
        if doc and bp._clear_stale_streaming(doc):
            self._write_session(hub, doc)
        return wsp.session_pulse(doc, self.config.default_checklist)

    def start_session(
        self,
        hub: Path,
        user_message: str,
        *,
        internet_research: bool = True,
    ) -> dict[str, Any]:
        bp.load_dotenv(hub)
        doc = {
            "version": 1,
            "kind": self.config.kind,
            "tabId": self.config.tab_id,
            "phase": "qna",
            "internetResearch": bool(internet_research),
            "messages": [{"role": "user", "content": user_message}],
            "brief": None,
            "researchBundle": None,
            "checklist": [dict(c) for c in self.config.default_checklist],
            "cursorWorking": False,
            "helpSeen": True,
            "createdUtc": _utc(),
        }
        return self._qna_turn(hub, doc, bootstrap=True)

    def chat_turn(self, hub: Path, user_message: str) -> dict[str, Any]:
        bp.load_dotenv(hub)
        doc = self._read_session(hub)
        if not doc:
            raise RuntimeError(f"No {self.config.tab_id} session — call start first")
        if doc.get("phase") not in ("qna",):
            raise RuntimeError(f"Tab not in Q&A (phase={doc.get('phase')})")
        if wsp.session_busy(doc):
            raise RuntimeError("Wait for the current reply to finish")
        doc.setdefault("messages", []).append({"role": "user", "content": user_message})
        doc["helpSeen"] = True
        return self._qna_turn(hub, doc, bootstrap=False)

    def resume_qna(self, hub: Path) -> dict[str, Any]:
        bp.load_dotenv(hub)
        doc = self._read_session(hub)
        if not doc:
            raise RuntimeError("No session")
        if doc.get("phase") == "cancelled":
            doc["phase"] = "qna"
        return self._qna_turn(hub, doc, bootstrap=False)

    def _qna_turn(self, hub: Path, doc: dict[str, Any], *, bootstrap: bool = False) -> dict[str, Any]:
        wc.ensure_checklist_shape(doc, self.config.default_checklist)
        user_before = wsp.latest_user_message(doc)

        if bootstrap and doc.get("internetResearch") and not doc.get("researchBundle"):
            doc["cursorWorking"] = True
            doc["phase"] = "research"
            self._write_session(hub, doc)
            try:
                wtr.maybe_run_session_research(hub, self.config.tab_id, doc, user_before)
            finally:
                doc["phase"] = "qna"
                doc["cursorWorking"] = False

        doc["cursorWorking"] = True
        self._write_session(hub, doc)
        try:
            llm_msgs = [{"role": m["role"], "content": m["content"]} for m in doc.get("messages", [])]
            raw = bp._llm_messages(
                hub, self._build_qna_system(doc), llm_msgs, stream_doc=doc, stream_role="assistant"
            )
            parsed = bp._parse_llm_json(raw)
            assistant = bp._sanitize_chat_content(parsed.get("assistantMessage") or "OK.")
            if not assistant.strip():
                assistant = bp._sanitize_chat_content(raw) or "OK."
            doc.setdefault("messages", []).append({"role": "assistant", "content": assistant})
            wc.merge_checklist(
                doc,
                parsed.get("checklist"),
                self.config.default_checklist,
                latest_user=user_before,
                allow_auto_fallback=False,
            )
            wc.merge_additions(doc, parsed.get("checklistAdditions"), self.config.default_checklist)
            brain_result = evaluate_tab_brain(self.config.tab_id, doc)
            if brain_result.reopen_checklist_ids:
                wc.reopen_items(doc, brain_result.reopen_checklist_ids, self.config.default_checklist)
            if brain_result.followup_additions:
                wc.merge_additions(
                    doc,
                    [
                        {"id": a.id, "label": a.label, "category": a.category}
                        for a in brain_result.followup_additions
                    ],
                    self.config.default_checklist,
                )
            move_on = bp._user_wants_move_on(doc)
            all_done = wc.all_done(doc, self.config.default_checklist)
            wants_complete = bool(parsed.get("qnaComplete"))
            if parsed.get("brief") and isinstance(parsed["brief"], dict):
                doc["brief"] = {**(doc.get("brief") or {}), **parsed["brief"]}
            if wants_complete and (all_done or move_on):
                if not doc.get("brief"):
                    doc["brief"] = parsed.get("brief") or {"tabId": self.config.tab_id}
                doc["phase"] = "awaiting_approval"
                doc.setdefault("messages", []).append(
                    {
                        "role": "assistant",
                        "content": (
                            f"**{self.config.title}** brief is ready for review. "
                            "Approve when ready — preview JSON is below the chat only at review time."
                        ),
                    }
                )
        except Exception as exc:
            doc.setdefault("messages", []).append(
                {
                    "role": "assistant",
                    "content": f"Assistant unavailable ({exc}). Check CURSOR_API_KEY in cave-grader/.env",
                }
            )
        finally:
            if not self._session_still_current(hub, doc):
                return self.public_session(hub)
            doc["cursorWorking"] = False
            doc.pop("streamingText", None)
            doc.pop("streamingRole", None)
            self._write_session(hub, doc)
        return self.public_session(hub)

    def approve_brief(self, hub: Path, approved: bool, feedback: str = "") -> dict[str, Any]:
        doc = self._read_session(hub)
        if not doc:
            raise RuntimeError("No session")
        if not approved:
            doc["phase"] = "qna"
            doc.setdefault("messages", []).append(
                {"role": "user", "content": feedback or "Please revise the brief."}
            )
            self._write_session(hub, doc)
            return self._qna_turn(hub, doc, bootstrap=False)

        brief = doc.get("brief") or {}
        if self.config.on_approve_brief:
            brief = self.config.on_approve_brief(brief)
        brief["version"] = 1
        brief["tabId"] = self.config.tab_id
        brief["finalizedUtc"] = _utc()
        atomic_write_text(self._brief_path(hub), json.dumps(brief, indent=2) + "\n")
        doc["phase"] = "finalized"
        doc["gradePassed"] = True
        note = self.config.brief_finalize_note or f"Brief written to `{self.config.brief_rel}`."
        doc.setdefault("messages", []).append({"role": "assistant", "content": note})
        self._write_session(hub, doc)
        try:
            from wizard_manifest import refresh_manifest

            refresh_manifest(hub)
        except Exception:
            pass
        try:
            from wizard_tab_apply import notify_tab_approved

            notify_tab_approved(hub, self.config.tab_id)
        except Exception:
            pass
        return self.public_session(hub)

    def reset_session(self, hub: Path) -> dict[str, Any]:
        fresh = {
            "version": 1,
            "kind": self.config.kind,
            "tabId": self.config.tab_id,
            "phase": "idle",
            "internetResearch": True,
            "messages": [],
            "brief": None,
            "researchBundle": None,
            "checklist": [dict(c) for c in self.config.default_checklist],
            "cursorWorking": False,
            "autoRespondActive": False,
            "autoRespondPreset": None,
            "helpSeen": False,
            "createdUtc": _utc(),
        }
        self._write_session(hub, fresh)
        try:
            from wizard_manifest import refresh_manifest

            refresh_manifest(hub)
        except Exception:
            pass
        return self.public_session(hub)

    def _preset_or_raise(self, preset: str) -> dict[str, Any]:
        key = (preset or "").strip().lower()
        if key not in self.config.presets:
            raise RuntimeError(f"Unknown preset {preset!r}")
        return self.config.presets[key]

    def _wait_until_idle(self, hub: Path, doc: dict[str, Any]) -> dict[str, Any]:
        if not wsp.session_busy(doc):
            return doc
        return self._read_session(hub) or doc

    def _generate_auto_reply(self, hub: Path, doc: dict[str, Any], preset: str) -> str:
        spec = self._preset_or_raise(preset)
        next_id = wc.next_pending_id(doc, self.config.default_checklist) or self.config.default_checklist[0]["id"]
        hint = self.config.topic_hints.get(next_id, "")
        world_state = {}
        try:
            world_state = wss.build_world_state(hub)
        except Exception:
            world_state = {}
        if world_state:
            doc["worldState"] = world_state
        world_block = wss.build_prompt_block(world_state) if world_state else ""
        system = (
            f"You simulate the human author answering the {self.config.title} interview.\n"
            f"Preset: {spec['title']} — {spec.get('description', '')}\n"
            f"Answer ONLY checklist topic [{next_id}] ({wc.label_for_id(doc, next_id, self.config.default_checklist)}).\n"
            f"{hint}\nPlain conversational text — no JSON, no bullet metadata.\n"
            f"{world_block}\nUse world-state centers/slots when mentioning placement.\n"
        )
        planner_msg = ""
        for m in reversed(doc.get("messages") or []):
            if m.get("role") == "assistant":
                planner_msg = bp._sanitize_chat_content(str(m.get("content") or ""))
                break
        user_prompt = f"Planner asked:\n{planner_msg or 'Start the interview.'}\n\nWrite one user reply."
        doc["cursorWorking"] = True
        doc["streamingRole"] = "user"
        self._write_session(hub, doc)
        try:
            reply = bp._llm_messages(
                hub,
                system,
                [{"role": "user", "content": user_prompt}],
                stream_doc=doc,
                stream_role="user",
                json_response=False,
            ).strip()
        finally:
            doc = self._read_session(hub) or doc
            doc["cursorWorking"] = False
            doc.pop("streamingText", None)
            doc.pop("streamingRole", None)
            self._write_session(hub, doc)
        return reply

    def _auto_respond_one_turn(self, hub: Path, preset: str) -> dict[str, Any]:
        doc = self._read_session(hub) or {}
        phase = doc.get("phase")
        if phase == "awaiting_approval":
            doc["autoRespondActive"] = False
            self._write_session(hub, doc)
            return {"done": True, "session": self.public_session(hub)}
        if phase == "finalized":
            return {"done": True, "session": self.public_session(hub)}
        if phase != "qna":
            raise RuntimeError(f"Auto-respond stopped in phase {phase}")
        if wsp.session_busy(doc):
            return {"done": False, "waiting": True, "session": self.public_session(hub)}

        doc["autoRespondActive"] = True
        doc["autoRespondPreset"] = preset
        self._write_session(hub, doc)

        if wc.all_done(doc, self.config.default_checklist):
            self.chat_turn(hub, "move on — finalize the brief for review.")
        else:
            reply = self._generate_auto_reply(hub, doc, preset)
            if not reply:
                raise RuntimeError("AI responder returned empty text")
            self.chat_turn(hub, reply)

        doc = self._read_session(hub) or {}
        done = doc.get("phase") in ("awaiting_approval", "finalized")
        if done:
            doc["autoRespondActive"] = False
        self._write_session(hub, doc)
        return {"done": done, "session": self.public_session(hub)}

    def auto_respond_step(self, hub: Path, preset: str, *, internet_research: bool = True) -> dict[str, Any]:
        bp.load_dotenv(hub)
        spec = self._preset_or_raise(preset)
        doc = self._read_session(hub)
        if not doc or doc.get("phase") in (None, "idle", "cancelled"):
            self.start_session(hub, spec["kickoff"], internet_research=internet_research)
        else:
            # If the user toggled research ON after the session started, honor it.
            if internet_research and not doc.get("internetResearch"):
                doc["internetResearch"] = True
            if internet_research and not doc.get("researchBundle"):
                user_before = wsp.latest_user_message(doc)
                doc["cursorWorking"] = True
                doc["phase"] = "research"
                self._write_session(hub, doc)
                try:
                    wtr.maybe_run_session_research(hub, self.config.tab_id, doc, user_before)
                finally:
                    doc["phase"] = "qna"
                    doc["cursorWorking"] = False
                self._write_session(hub, doc)
            else:
                self._write_session(hub, doc)
        return self._auto_respond_one_turn(hub, preset)

    def auto_respond_until_approval(
        self, hub: Path, preset: str, *, internet_research: bool = True
    ) -> dict[str, Any]:
        bp.load_dotenv(hub)
        self._preset_or_raise(preset)
        doc = self._read_session(hub)
        if not doc or doc.get("phase") in (None, "idle", "cancelled"):
            self.start_session(
                hub, self._preset_or_raise(preset)["kickoff"], internet_research=internet_research
            )
        for _ in range(32):
            doc = self._read_session(hub) or {}
            if wsp.session_busy(doc):
                continue
            result = self._auto_respond_one_turn(hub, preset)
            if result.get("done"):
                sess = result.get("session") or self.public_session(hub)
                if sess.get("phase") == "awaiting_approval":
                    return self.approve_brief(hub, True, "")
                return sess
            if not result.get("waiting"):
                continue
        return self.public_session(hub)

    def export_bundle(self, hub: Path) -> dict[str, Any]:
        doc = self._read_session(hub)
        if not doc or doc.get("phase") != "finalized" or not doc.get("gradePassed"):
            raise RuntimeError("Tab must be finalized and passed before export")
        from wizard_export import write_tab_export

        return write_tab_export(hub, self.config, doc)
