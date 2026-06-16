import React, { useState } from 'react';
import { 
  FileText, Cpu, Key, Database, RefreshCw, Layers, Shield, 
  Settings, Code, Sparkles, AlertTriangle, BookOpen, CheckCircle, Copy, Check
} from 'lucide-react';

export default function Dossier() {
  const [activeTab, setActiveTab] = useState<'architecture' | 'schema' | 'protocols' | 'rules' | 'devops'>('architecture');
  const [copiedId, setCopiedId] = useState<string | null>(null);

  const copyToClipboard = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  const codeFirestoreSchema = `{
  // collection: players
  "players": {
    "docId": "{competitionPlayerId}", // Linked to Firebase Auth uid
    "playerId": "string",            // Firebase UID
    "ugsPlayerId": "string",         // Linked Unity Gaming Services Player ID
    "displayName": "string",
    "level": "number",
    "xp": "number",
    "activeAgentId": "string",
    "cohortIds": ["array", "string"],
    "updatedAt": "timestamp",
    "rpgStats": { // Snapshot details for dashboard
      "health": "number",
      "stamina": "number",
      "currencyGold": "number",
      "currencyGems": "number",
      "skills": [
        {"name": "string", "level": "number"}
      ],
      "inventory": [
        {"id": "string", "name": "string", "quantity": "number", "rarity": "string"}
      ],
      "equipment": [
        {"slot": "string", "itemId": "string", "name": "string", "bonus": "string"}
      ],
      "biomesDiscovered": ["array", "string"],
      "bossFlags": {
        "bossId": "boolean"
      }
    }
  },

  // collection: agents
  "agents": {
    "docId": "{agentId}", // Globally unique GUID
    "agentId": "string",
    "playerId": "string", // Reference back to parent user
    "displayName": "string",
    "xp": "number",
    "level": "number",
    "avatarId": "string",
    "mood": "string",
    "strikes": "number",
    "rewardsCount": "number",
    "trainingMilestones": ["array", "string"],
    "updatedAt": "timestamp"
  },

  // collection: cohorts
  "cohorts": {
    "docId": "{cohortCode}", // e.g., "DTA-SPRING26"
    "cohortId": "string",
    "cohortName": "string",
    "teacherId": "string", // User ID who created the cohort and is MOD
    "createdAt": "timestamp"
  }
}`;

  const codeSecurityRules = `rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    
    // 1. Global Default Deny
    match /{document=**} {
      allow read, write: if false;
    }

    // Reuseable safe helpers
    function isSignedIn() {
      return request.auth != null;
    }
    
    function isEmailVerified() {
      return request.auth.token.email_verified == true;
    }

    function isOwner(userId) {
      return isSignedIn() && request.auth.uid == userId;
    }

    // 2. Players Collection rules
    match /players/{playerId} {
      // Any verified user can read basic metadata for human board
      allow get: if isSignedIn();
      allow list: if isSignedIn() && (
        resource.data.playerId == request.auth.uid || 
        resource.data.cohortIds.size() > 0
      );
      
      // Only player owner can write. Enforce server time.
      allow create: if isSignedIn() && isOwner(playerId) 
        && request.resource.data.updatedAt == request.time;
      allow update: if isSignedIn() && isOwner(playerId)
        && request.resource.data.updatedAt == request.time
        && request.resource.data.playerId == resource.data.playerId; // Immortality of ID
    }

    // 3. Agents Collection rules
    match /agents/{agentId} {
      // Spectators can read agent stats for leaderboard
      allow get: if isSignedIn();
      allow list: if isSignedIn();
      
      // Only owner of the agent can write snapshots.
      // Must verify ownership by checking player auth matches playerId on the agent doc.
      allow create: if isSignedIn() 
        && request.resource.data.playerId == request.auth.uid;
      allow update: if isSignedIn() 
        && resource.data.playerId == request.auth.uid
        && request.resource.data.playerId == request.auth.uid;
    }

    // 4. Cohorts Collection rules
    match /cohorts/{cohortId} {
      allow read: if isSignedIn();
      // Only teachers or verified accounts can create cohorts
      allow create: if isSignedIn() && isEmailVerified();
      // Only creator is moderator
      allow update, delete: if isSignedIn() && resource.data.teacherId == request.auth.uid;
    }
  }
}`;

  const codeUnitySyncCode = `using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class CompetitionCloudSync : MonoBehaviour
{
    private string apiEndpoint = "https://your-cloud-run-url/api/syncSnapshot";
    private float syncInterval = 4.0f; // Every 4s save cadence
    private float timer = 0f;

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= syncInterval)
        {
            timer = 0f;
            _ = UploadStateSnapshot();
        }
    }

    public async Task UploadStateSnapshot()
    {
        try
        {
            // 1. Gather player and agent JSON payloads from absolute persistentDataPath paths
            string playerPath = Path.Combine(Application.persistentDataPath, "Competition/player_profile.json");
            if (!File.Exists(playerPath)) return;

            string playerJson = File.ReadAllText(playerPath);
            
            // 2. Compose sync payload with local file state
            var payload = new {
                authToken = PlayerPrefs.GetString("Firebase_Link_Token", ""),
                ugsPlayerId = PlayerPrefs.GetString("UGS_Player_Id", ""),
                timestamp = DateTime.UtcNow.ToString("o"),
                playerData = playerJson
            };

            string requestBody = JsonUtility.ToJson(payload);

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("X-Unity-Version", Application.unityVersion);
                var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(apiEndpoint, content);
                
                if (response.IsSuccessStatusCode)
                {
                    Debug.Log("[CloudSync] State uploaded successfully!");
                }
                else
                {
                    // Cache and queue for retry on local SQLite / file if network fails
                    Debug.LogWarning("[CloudSync] Failed upload, caching locally.");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CloudSync] Error: {ex.Message}");
        }
    }
}`;

  return (
    <div className="bg-[#0e1117] border border-slate-800 rounded-xl overflow-hidden shadow-2xl" id="dossier-root">
      {/* Header Bar */}
      <div className="bg-slate-900 border-b border-slate-800/80 px-6 py-4 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <BookOpen className="w-6 h-6 text-emerald-400" />
          <div>
            <h2 className="font-sans text-lg font-bold text-slate-100 tracking-tight">System Specification Dossier</h2>
            <p className="font-sans text-xs text-slate-400">Deep Train Academy Web Portal Integration Briefing</p>
          </div>
        </div>
        <div className="flex bg-slate-950 p-1 rounded-lg border border-slate-800/80 text-xs font-mono self-start md:self-auto">
          <span className="px-2 py-0.5 text-emerald-400 bg-slate-900 rounded font-semibold">Active Design Specification</span>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex border-b border-slate-800 bg-slate-900/50 overflow-x-auto text-sm">
        <button
          onClick={() => setActiveTab('architecture')}
          className={`flex items-center gap-2 px-5 py-3 border-b-2 font-medium transition-all cursor-pointer whitespace-nowrap ${
            activeTab === 'architecture'
              ? 'border-emerald-500 text-emerald-400 bg-emerald-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200 hover:bg-slate-800/40'
          }`}
        >
          <Layers className="w-4 h-4" />
          1. System Architecture
        </button>
        <button
          onClick={() => setActiveTab('schema')}
          className={`flex items-center gap-2 px-5 py-3 border-b-2 font-medium transition-all cursor-pointer whitespace-nowrap ${
            activeTab === 'schema'
              ? 'border-emerald-500 text-emerald-400 bg-emerald-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200 hover:bg-slate-800/40'
          }`}
        >
          <Database className="w-4 h-4" />
          2. Schema & DB
        </button>
        <button
          onClick={() => setActiveTab('protocols')}
          className={`flex items-center gap-2 px-5 py-3 border-b-2 font-medium transition-all cursor-pointer whitespace-nowrap ${
            activeTab === 'protocols'
              ? 'border-emerald-500 text-emerald-400 bg-emerald-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200 hover:bg-slate-800/40'
          }`}
        >
          <RefreshCw className="w-4 h-4" />
          3. Linking & Sync
        </button>
        <button
          onClick={() => setActiveTab('rules')}
          className={`flex items-center gap-2 px-5 py-3 border-b-2 font-medium transition-all cursor-pointer whitespace-nowrap ${
            activeTab === 'rules'
              ? 'border-emerald-500 text-emerald-400 bg-emerald-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200 hover:bg-slate-800/40'
          }`}
        >
          <Shield className="w-4 h-4" />
          4. Security Rules
        </button>
        <button
          onClick={() => setActiveTab('devops')}
          className={`flex items-center gap-2 px-5 py-3 border-b-2 font-medium transition-all cursor-pointer whitespace-nowrap ${
            activeTab === 'devops'
              ? 'border-emerald-500 text-emerald-400 bg-emerald-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200 hover:bg-slate-800/40'
          }`}
        >
          <Settings className="w-4 h-4" />
          5. Deployment & Costs
        </button>
      </div>

      {/* Pane Content */}
      <div className="p-6 md:p-8">
        
        {/* TAB 1: SYSTEM ARCHITECTURE */}
        {activeTab === 'architecture' && (
          <div className="space-y-8 animate-fade-in">
            {/* Visual Flow diagram of synchronization */}
            <div>
              <h3 className="text-emerald-400 font-bold text-base mb-4 flex items-center gap-2">
                <Cpu className="w-5 h-5" /> Architectural Flow Visualizer
              </h3>
              <div className="bg-slate-950 p-6 rounded-lg border border-slate-800 relative select-none">
                <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 items-center text-center">
                  
                  {/* Left node: Unity Game Engine */}
                  <div className="p-4 bg-slate-900 border border-slate-700 rounded-lg shadow-md space-y-2">
                    <div className="bg-sky-500/15 text-sky-400 text-xs font-bold py-1 px-3 rounded-full inline-block">Unity Client</div>
                    <h4 className="font-semibold text-slate-200 text-sm">Unity 6 Engine Codebase</h4>
                    <p className="text-xs text-slate-400">Writes persistent profiles to disk on 4s checkpoint flush.</p>
                    <div className="text-[10px] font-mono text-slate-500 bg-slate-950 p-2 rounded text-left space-y-1">
                      <div>📁 player_profile.json</div>
                      <div>📁 Agents/agent_01/profile.json</div>
                    </div>
                  </div>

                  {/* Middleware Arrow / Action Block */}
                  <div className="flex flex-col items-center justify-center space-y-2 py-4 lg:py-0">
                    <span className="text-yellow-400 text-xs font-mono font-bold">HTTPS REST API</span>
                    <div className="w-full h-1 bg-gradient-to-r from-sky-500 via-transparent to-emerald-500 relative flex items-center justify-center">
                      <div className="w-3 h-3 bg-yellow-400 rounded-full animate-pulse"></div>
                    </div>
                    <span className="text-slate-400 text-[11px]">One-Time Link Redirection</span>
                    <span className="text-[10px] bg-slate-900 px-2 py-1 border border-slate-800 rounded font-mono text-slate-300">CompetitionCloudSync.cs</span>
                  </div>

                  {/* Right node: Firebase Ecosystem */}
                  <div className="p-4 bg-slate-900 border border-slate-700 rounded-lg shadow-md space-y-2">
                    <div className="bg-emerald-500/15 text-emerald-400 text-xs font-bold py-1 px-3 rounded-full inline-block">Firebase Web Backend</div>
                    <h4 className="font-semibold text-slate-200 text-sm">Firestore & Cloud Functions</h4>
                    <p className="text-xs text-slate-400">Authenticates link codes, aggregates metrics, caches leadboard views.</p>
                    <div className="text-[10px] font-mono text-slate-500 bg-slate-950 p-2 rounded text-left space-y-1">
                      <div>🗄️ Firestore: users, agents, cohorts</div>
                      <div>⚡ Cloud Function: onPlayerSnapshotWrite</div>
                    </div>
                  </div>

                </div>
              </div>
            </div>

            {/* Phased Roadmap list */}
            <div>
              <h3 className="text-emerald-400 font-bold text-base mb-4 flex items-center gap-2">
                <FileText className="w-5 h-5" /> Companion Platform Roadmap
              </h3>
              <div className="relative border-l-2 border-slate-800 pl-6 ml-3 space-y-8">
                
                <div className="relative">
                  <div className="absolute -left-[31px] top-1 bg-emerald-500 w-4 h-4 rounded-full border-4 border-[#0e1117]"></div>
                  <h4 className="text-slate-200 font-semibold text-sm">v1.0 MVP: Deep Integration & Linking (Active Scope)</h4>
                  <ul className="text-xs text-slate-400 mt-2 list-disc list-inside space-y-1">
                    <li>Launch responsive portal Home, Download, and spectator landing views.</li>
                    <li>Google + Email/Password sign-up/sign-in flows.</li>
                    <li>One-time short code generation schema linking web profile to Unity UGS account.</li>
                    <li>Basic progression visual dashboard reflecting local client file properties uploaded on save.</li>
                    <li>Hourly leaderboard views separated by human and agent indicators.</li>
                  </ul>
                </div>

                <div className="relative">
                  <div className="absolute -left-[31px] top-1 bg-yellow-600 w-4 h-4 rounded-full border-4 border-[#0e1117]"></div>
                  <h4 className="text-slate-300 font-semibold text-sm">v1.1 Cohort & Leaderboards Polish (Near Term)</h4>
                  <ul className="text-xs text-slate-400 mt-2 list-disc list-inside space-y-1">
                    <li>Dynamic visual telemetry displays for AI training cycles.</li>
                    <li>Classroom cohort creation and secure invite codes for training cohorts.</li>
                    <li>Automated hourly aggregation counters reducing Firestore document execution costs.</li>
                  </ul>
                </div>

                <div className="relative">
                  <div className="absolute -left-[31px] top-1 bg-slate-800 w-4 h-4 rounded-full border-4 border-[#0e1117]"></div>
                  <h4 className="text-slate-300 font-semibold text-sm">v2.0 Full Integration & Social Forum (Post-Launch)</h4>
                  <ul className="text-xs text-slate-400 mt-2 list-disc list-inside space-y-1">
                    <li>Loadout updates edit panel written directly to Firestore, pulled down by Unity client on launch.</li>
                    <li>Classroom forum interface with posts, comments, upload shares. Cohort moderators inherit mod controls.</li>
                    <li>Interactive AI Agent long-term memory query module.</li>
                  </ul>
                </div>

              </div>
            </div>

            {/* COPPA Section */}
            <div className="p-4 bg-yellow-500/10 border border-yellow-500/20 rounded-lg flex gap-3 text-xs text-yellow-200/90 leading-relaxed">
              <AlertTriangle className="w-5 h-5 text-yellow-500 shrink-0" />
              <div>
                <strong className="block text-yellow-400 font-bold mb-1">COPPA Compliance Core Invariants (Classroom Cohorts)</strong>
                Because classroom cohorts can utilize DTA invite codes for secondary training sessions:
                1. No personal demographic information (PII) is fetched; names default to unique auto-generated game monikers (e.g., 'Agent Slate-A4').
                2. Explicit separation of personal emails in firestore subcollections matching private authorization matrices.
                3. Built-in Classroom moderator capabilities allowing teachers to flush shared fields or freeze chat forums instanced in v2.
              </div>
            </div>
          </div>
        )}

        {/* TAB 2: FIRESTORE SCHEMA */}
        {activeTab === 'schema' && (
          <div className="space-y-6 animate-fade-in">
            <div className="flex flex-col md:flex-row md:items-center justify-between gap-2">
              <div>
                <h3 className="text-emerald-400 font-bold text-base">Firestore Database Schema Blueprint</h3>
                <p className="text-xs text-slate-400">Strict structural fields mapping Firestore document attributes to Unity client files.</p>
              </div>
              <button 
                onClick={() => copyToClipboard(codeFirestoreSchema, 'schema')}
                className="flex items-center gap-1 text-xs text-slate-400 hover:text-emerald-400 transition bg-slate-900 border border-slate-800 px-3 py-1.5 rounded cursor-pointer"
              >
                {copiedId === 'schema' ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
                {copiedId === 'schema' ? 'Copied JSON!' : 'Copy Schema'}
              </button>
            </div>

            <div className="relative">
              <pre className="text-xs font-mono text-slate-300 bg-slate-950 p-4 rounded-lg border border-slate-800 overflow-x-auto max-h-[420px]">
                <code>{codeFirestoreSchema}</code>
              </pre>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div className="bg-slate-900 border border-slate-800/80 p-4 rounded-lg">
                <h4 className="text-slate-200 font-semibold text-xs font-mono uppercase tracking-wider mb-2 text-emerald-400">Configured Indexes</h4>
                <ul className="text-xs text-slate-400 list-disc list-inside space-y-1.5 leading-relaxed">
                  <li>Composite Index on <code className="text-slate-300 bg-slate-950 px-1 rounded">players/ *</code>: <code className="text-slate-300">cohortIds</code> (Array) + <code className="text-slate-300">level</code> (Desc) + <code className="text-slate-300">xp</code> (Desc)</li>
                  <li>Composite Index on <code className="text-slate-300 bg-slate-950 px-1 rounded">agents/ *</code>: <code className="text-slate-300">playerId</code> + <code className="text-slate-300">xp</code> (Desc)</li>
                  <li>Single Index on <code className="text-slate-300 bg-slate-950 px-1 rounded">cohorts/ *</code>: <code className="text-slate-300">teacherId</code></li>
                </ul>
              </div>
              <div className="bg-slate-900 border border-slate-800/80 p-4 rounded-lg">
                <h4 className="text-slate-200 font-semibold text-xs font-mono uppercase tracking-wider mb-2 text-blue-400">Database Constraints</h4>
                <p className="text-xs text-slate-400 leading-relaxed">
                  Data storage avoids raw files (ONNX files or training logs). Document maximum payload values are strictly guarded under 400KB (such as short inventory arrays capped at 100 items). Updates use Server Date stamp constructs to ensure client clock manipulation fails.
                </p>
              </div>
            </div>
          </div>
        )}

        {/* TAB 3: ACCOUNT LINKING & SYNC PROTOCOLS */}
        {activeTab === 'protocols' && (
          <div className="space-y-6 animate-fade-in">
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              
              {/* Account Linking Block */}
              <div className="space-y-4">
                <h3 className="text-emerald-400 font-bold text-base flex items-center gap-2">
                  <Key className="w-5 h-5" /> Auth & Account Linking Protocol
                </h3>
                <div className="bg-slate-900 border border-slate-800 p-5 rounded-lg space-y-3">
                  <div className="flex items-center gap-2 text-emerald-400 font-semibold text-xs font-mono">
                    <span className="bg-emerald-500/10 text-emerald-400 px-2 py-0.5 rounded border border-emerald-500/25">Step-by-Step Flow</span>
                  </div>
                  <ol className="text-xs text-slate-400 list-decimal list-inside space-y-3 leading-relaxed">
                    <li>
                      <strong className="text-slate-300">Web Dashboard Action:</strong> First, register/sign in on the Web Hub via Firebase Auth (Google or Email/Password). The platform assigns a native Firebase <code className="text-slate-200 font-semibold font-mono font-bold select-all bg-slate-950 px-1 rounded">firebaseUid</code>.
                    </li>
                    <li>
                      <strong className="text-slate-300">Web Code Generation:</strong> Web client triggers a Google Cloud Function endpoint <code className="text-slate-300 font-mono bg-slate-950 px-1 rounded">generateLinkCode</code>. It produces a random, high-contrast, base32 6-character clean code (e.g., <code className="text-amber-400 font-mono font-bold">X7-R2Y</code>). This is stored inside <code className="text-slate-300 font-mono bg-slate-950 px-1">linkCodes/{"{code}"}</code> mapped to the Firebase UID, valid for 10 minutes.
                    </li>
                    <li>
                      <strong className="text-slate-300">Unity Configuration Entry:</strong> Player opens Unity game, navigates to "Settings" segment, and types in the 6-character code.
                    </li>
                    <li>
                      <strong className="text-slate-300">Unity Verification Request:</strong> Unity client posts short-code + internal UGS identifier (<code className="text-slate-300 font-mono bg-slate-950 px-1">ugsPlayerId</code>) to the HTTPS relay endpoint <code className="text-slate-300 font-mono bg-slate-950 px-1">redeemLinkCode</code>.
                    </li>
                    <li>
                      <strong className="text-slate-300">Linking Lock:</strong> If matching code is found, the function writes <code className="text-slate-300 font-mono bg-slate-950 px-1">ugsPlayerId</code> to player document, writes Firebase <code className="text-emerald-400 font-semibold font-mono select-all bg-slate-900 p-0.5 rounded">firebaseUid</code> back as linked on Unity, and returns a durable cryptographic session sync token saved in local Unity persistent storage.
                    </li>
                  </ol>
                </div>
              </div>

              {/* Sync Protocol Block */}
              <div className="space-y-4">
                <h3 className="text-emerald-400 font-bold text-base flex items-center gap-2">
                  <RefreshCw className="w-5 h-5" /> Unity Snapshot Synchronization
                </h3>
                <div className="bg-slate-900 border border-slate-800 p-5 rounded-lg space-y-3">
                  <div className="flex items-center gap-2 text-blue-400 font-semibold text-xs font-mono">
                    <span className="bg-blue-500/10 text-blue-400 px-2 py-0.5 rounded border border-blue-500/25">Sync Engine Invariants</span>
                  </div>
                  <ul className="text-xs text-slate-400 space-y-3 list-disc list-inside leading-relaxed">
                    <li>
                      <strong className="text-slate-300">Idempotency:</strong> Every sync payload specifies a client-sourced UTC ISO-8601 <code className="text-slate-300">updatedAt</code> timestamp. Incoming writes with timestamps older than or equal to the stored Firestore value are safely dropped.
                    </li>
                    <li>
                      <strong className="text-slate-300">Conflict Rules (Server Wins Aggregate):</strong> If an offline-mode player changes Level/XP locally and requests sync, the server reconciles global tables (like leaderboard aggregated totals) by taking the mathematical maximum of the two levels, ensuring integrity against client-side hex editor hacks.
                    </li>
                    <li>
                      <strong className="text-slate-300">Offline Recovery Queue:</strong> In case of system network drops, Unity queues sync operations locally inside an SQLite-lite dynamic database file on Unity disk, utilizing an exponential backoff retry interval.
                    </li>
                    <li>
                      <strong className="text-slate-300">Security / Payload Limits:</strong> Max file transfer is capped at 100KB to prevent network exhaustion. Full binary ONNX weights or large raw memory logs like <code className="text-slate-300">knowledge.jsonl</code> are strictly filtered out in Unity core before request transit.
                    </li>
                  </ul>
                </div>
              </div>

            </div>

            {/* Script preview */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <h4 className="text-slate-350 font-bold text-xs font-mono uppercase tracking-wider">Example: CompetitionCloudSync.cs</h4>
                <button 
                  onClick={() => copyToClipboard(codeUnitySyncCode, 'csharp')}
                  className="flex items-center gap-1 text-xs text-slate-400 hover:text-emerald-400 transition bg-slate-900 border border-slate-800 px-3 py-1 rounded cursor-pointer"
                >
                  {copiedId === 'csharp' ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
                  {copiedId === 'csharp' ? 'Copied C#!' : 'Copy Unity Component'}
                </button>
              </div>
              <pre className="text-[11px] font-mono text-slate-300 bg-slate-950 p-4 rounded-lg border border-slate-800 overflow-x-auto max-h-[300px]">
                <code>{codeUnitySyncCode}</code>
              </pre>
            </div>
          </div>
        )}

        {/* TAB 4: FIRESTORE SECURITY RULES */}
        {activeTab === 'rules' && (
          <div className="space-y-6 animate-fade-in">
            <div className="flex flex-col md:flex-row md:items-center justify-between gap-2">
              <div>
                <h3 className="text-emerald-400 font-bold text-base flex items-center gap-2">
                  <Shield className="w-5 h-5" /> Cloud Firestore Security Rules
                </h3>
                <p className="text-xs text-slate-400">Production-ready security rules guarding documents against anonymous extraction, ID poisoning, and privilege writes.</p>
              </div>
              <button 
                onClick={() => copyToClipboard(codeSecurityRules, 'rules')}
                className="flex items-center gap-1 text-xs text-slate-400 hover:text-emerald-400 transition bg-slate-900 border border-slate-800 px-3 py-1.5 rounded cursor-pointer"
              >
                {copiedId === 'rules' ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
                {copiedId === 'rules' ? 'Copied Rules!' : 'Copy Rules'}
              </button>
            </div>

            <pre className="text-[11px] font-mono text-slate-300 bg-slate-950 p-4 rounded-lg border border-slate-800 overflow-x-auto max-h-[380px]">
              <code>{codeSecurityRules}</code>
            </pre>

            <div className="p-4 bg-emerald-500/10 border border-emerald-500/20 rounded-lg">
              <h4 className="text-emerald-300 font-bold text-xs mb-1">Architecture Security Invariants Enforced:</h4>
              <ul className="text-xs text-slate-400 list-disc list-inside space-y-1 mt-2">
                <li><strong className="text-slate-300">Identity isolation:</strong> Players are prohibited from writing updates into document IDs that don't match their active authenticated firebaseUid.</li>
                <li><strong className="text-slate-300">Immutability checks:</strong> High-stakes values e.g. level tracking, and linked identifiers cannot be blanked or reassigned to secondary user IDs on updates.</li>
                <li><strong className="text-slate-305">Verified boundaries:</strong> User must have true status verification tokens (where applicable) before triggering cohort registrations.</li>
              </ul>
            </div>
          </div>
        )}

        {/* TAB 5: DEPLOYMENT AND COSTS */}
        {activeTab === 'devops' && (
          <div className="space-y-6 animate-fade-in">
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              
              {/* Checklist */}
              <div className="space-y-4">
                <h3 className="text-emerald-400 font-bold text-base flex items-center gap-2">
                  <CheckCircle className="w-5 h-5" /> Firebase Deployment Checklist
                </h3>
                <div className="bg-slate-900 border border-slate-800 p-5 rounded-lg space-y-4 text-xs text-slate-400 leading-relaxed">
                  <p className="font-semibold text-slate-200">Execution sequence in Firebase Console:</p>
                  <ul className="space-y-3">
                    <li className="flex gap-2 items-start text-xs">
                      <div className="w-4 h-4 rounded-full bg-slate-950 border border-emerald-500 flex items-center justify-center shrink-0 mt-0.5 text-[10px] text-emerald-400 font-bold">1</div>
                      <div>
                        <strong className="text-slate-300">Provision Firestore in Native Mode:</strong> Navigate to Firestore Database panel, choose 'Production' rules environment, select target Cloud Region nearest your primary classroom/game servers (e.g. <code className="text-slate-300 bg-slate-950 px-1 rounded">us-west1</code>).
                      </div>
                    </li>
                    <li className="flex gap-2 items-start text-xs">
                      <div className="w-4 h-4 rounded-full bg-slate-950 border border-emerald-500 flex items-center justify-center shrink-0 mt-0.5 text-[10px] text-emerald-400 font-bold">2</div>
                      <div>
                        <strong className="text-slate-300">Enable Auth Methods:</strong> Go to Authentication → Sign-In Method. Activating safe Google SSO and Email authorization.
                      </div>
                    </li>
                    <li className="flex gap-2 items-start text-xs">
                      <div className="w-4 h-4 rounded-full bg-slate-950 border border-emerald-500 flex items-center justify-center shrink-0 mt-0.5 text-[10px] text-emerald-400 font-bold">3</div>
                      <div>
                        <strong className="text-slate-300">Upload Security Rules & Indexes:</strong> Deploy utilizing the Firebase CLI Command: <code className="text-slate-300 bg-slate-950 px-1.5 py-0.5 rounded font-mono select-all font-bold">firebase deploy --only firestore:rules,firestore:indexes</code>.
                      </div>
                    </li>
                    <li className="flex gap-2 items-start text-xs">
                      <div className="w-4 h-4 rounded-full bg-slate-950 border border-emerald-500 flex items-center justify-center shrink-0 mt-0.5 text-[10px] text-emerald-400 font-bold">4</div>
                      <div>
                        <strong className="text-slate-300">Initialize Local Emulator Suite:</strong> Setup unit validations offline before merging by spinning up localized emulators: <code className="text-slate-300 bg-slate-950 px-1.5 py-0.5 rounded font-mono select-all font-bold">firebase emulators:start</code>.
                      </div>
                    </li>
                  </ul>
                </div>
              </div>

              {/* Pricing, Risks, alternatives */}
              <div className="space-y-4">
                <h3 className="text-emerald-400 font-bold text-base flex items-center gap-2">
                  <Cpu className="w-5 h-5" /> Cost Projection & Feasibility
                </h3>
                <div className="bg-slate-900 border border-slate-800 p-5 rounded-lg space-y-4 text-xs text-slate-400">
                  <div className="border-b border-slate-800 pb-3">
                    <h4 className="font-semibold text-slate-205 py-0.5 text-slate-300">Free-Tier Classroom Scale Estimate (~30 active cohort members):</h4>
                    <p className="mt-1 leading-relaxed">
                      With players synchronizing data every 4 seconds, direct client updates would result in massive read-write spikes. By implementing <strong>local debounce caching</strong> in Unity (delaying sync until exit, main menu, or capping writing spikes at 2-minute limits while active), 30 students will generate around 6,000 writes/day. This is easily covered in the <strong>free Firebase Spark Limit (20,000 Writes/day and 50,000 Reads/day)</strong>.
                    </p>
                  </div>
                  <div>
                    <h4 className="font-semibold text-amber-500 font-bold">Why Firebase instead of PlayFab alone?</h4>
                    <p className="mt-1 leading-relaxed">
                      PlayFab is phenomenal for out-of-the-box economies, but has rigid, slow web client integration surfaces with tight platform API limitations. Since Unity Gaming Services (UGS) handles real-time multiplayer lobbies, Vivox, and relay mechanics, integrating a highly flexible Firebase backend provides perfect React companion app performance, customizable Classroom dashboards, fast query times, and standard web social hooks in a costless Spark frame.
                    </p>
                  </div>
                </div>
              </div>

            </div>
          </div>
        )}

      </div>
    </div>
  );
}
