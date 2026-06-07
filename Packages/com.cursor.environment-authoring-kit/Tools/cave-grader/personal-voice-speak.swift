#!/usr/bin/env swift
/**
 * Synthesize macOS Personal Voice to WAV (say -o is silent without mysay.dylib).
 *
 *   swift personal-voice-speak.swift --list --personal-only
 *   swift personal-voice-speak.swift --authorize
 *   swift personal-voice-speak.swift -v "Jacob Adkins" -t "Hello" -o /tmp/test.wav
 */
import AVFoundation
import Foundation

@available(macOS 14.0, *)
func requestAuth() async -> AVSpeechSynthesizer.PersonalVoiceAuthorizationStatus {
    await withCheckedContinuation { cont in
        AVSpeechSynthesizer.requestPersonalVoiceAuthorization { status in
            cont.resume(returning: status)
        }
    }
}

@available(macOS 14.0, *)
func authDescription(_ auth: AVSpeechSynthesizer.PersonalVoiceAuthorizationStatus) -> String {
    switch auth {
    case .authorized:
        return "authorized — OK to synthesize"
    case .denied:
        return "denied — System Settings → Accessibility → Speech → Personal Voice → allow this app"
    case .notDetermined:
        return "notDetermined — run with --authorize and approve the dialog"
    case .unsupported:
        return "unsupported on this device"
    @unknown default:
        return "unknown (rawValue=\(auth.rawValue))"
    }
}

func isPersonal(_ voice: AVSpeechSynthesisVoice) -> Bool {
    if #available(macOS 14.0, *) {
        if voice.voiceTraits.contains(.isPersonalVoice) { return true }
    }
    let id = voice.identifier.lowercased()
    return id.contains("personalvoice") || id.contains("com.apple.speech.personalvoice")
}

@available(macOS 14.0, *)
func listVoices(personalOnly: Bool) async {
    let auth = await requestAuth()
    fputs("Personal Voice authorization: \(authDescription(auth))\n\n", stderr)
    for v in AVSpeechSynthesisVoice.speechVoices() {
        if personalOnly && !isPersonal(v) { continue }
        let tag = isPersonal(v) ? " [PERSONAL]" : ""
        print("\(v.name)\t\(v.identifier)\(tag)")
    }
}

func findVoice(named: String) -> AVSpeechSynthesisVoice? {
    let want = named.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    var partial: [AVSpeechSynthesisVoice] = []
    var personal: [AVSpeechSynthesisVoice] = []
    for v in AVSpeechSynthesisVoice.speechVoices() {
        let vn = v.name.lowercased()
        if vn == want { return v }
        if vn.contains(want) || want.contains(vn) { partial.append(v) }
        if isPersonal(v) {
            personal.append(v)
            let parts = want.split(separator: " ").map(String.init)
            if !parts.isEmpty && parts.allSatisfy({ vn.contains($0.lowercased()) }) {
                return v
            }
        }
    }
    if partial.count == 1 { return partial[0] }
    if !partial.isEmpty { return partial[0] }
    if personal.count == 1 { return personal[0] }
    return personal.first
}

@available(macOS 14.0, *)
func synthesizeToWav(text: String, voice: AVSpeechSynthesisVoice, outPath: String, rateMultiplier: Float = 0.88) throws {
    let utterance = AVSpeechUtterance(string: text)
    utterance.voice = voice
    utterance.rate = min(
        AVSpeechUtteranceMaximumSpeechRate,
        max(AVSpeechUtteranceMinimumSpeechRate, AVSpeechUtteranceDefaultSpeechRate * rateMultiplier)
    )
    utterance.pitchMultiplier = 1.0
    utterance.preUtteranceDelay = 0.02
    utterance.postUtteranceDelay = 0.05

    let synth = AVSpeechSynthesizer()
    let tmpURL = URL(fileURLWithPath: outPath).deletingPathExtension().appendingPathExtension("caf")
    try? FileManager.default.removeItem(at: tmpURL)

    let sem = DispatchSemaphore(value: 0)
    var writeError: Error?
    var audioFile: AVAudioFile?
    var framesWritten: UInt64 = 0

    synth.write(utterance) { buffer in
        if writeError != nil { return }
        if buffer == nil {
            sem.signal()
            return
        }
        guard let pcm = buffer as? AVAudioPCMBuffer, pcm.frameLength > 0 else { return }
        do {
            if audioFile == nil {
                audioFile = try AVAudioFile(forWriting: tmpURL, settings: pcm.format.settings)
            }
            try audioFile?.write(from: pcm)
            framesWritten += UInt64(pcm.frameLength)
        } catch {
            writeError = error
            sem.signal()
        }
    }

    if sem.wait(timeout: .now() + 55) == .timedOut {
        throw NSError(
            domain: "personal-voice-speak",
            code: 2,
            userInfo: [NSLocalizedDescriptionKey: "Timed out waiting for Personal Voice audio"]
        )
    }
    if let writeError { throw writeError }
    guard framesWritten > 0, audioFile != nil, FileManager.default.fileExists(atPath: tmpURL.path) else {
        throw NSError(
            domain: "personal-voice-speak",
            code: 3,
            userInfo: [
                NSLocalizedDescriptionKey:
                    "No audio captured — allow this app under System Settings → Accessibility → Speech → Personal Voice, then run: swift personal-voice-speak.swift --authorize",
            ]
        )
    }

    let proc = Process()
    proc.executableURL = URL(fileURLWithPath: "/usr/bin/env")
    proc.arguments = ["ffmpeg", "-y", "-i", tmpURL.path, "-ar", "48000", "-ac", "2", outPath]
    let errPipe = Pipe()
    proc.standardError = errPipe
    try proc.run()
    proc.waitUntilExit()
    if proc.terminationStatus != 0 {
        let errData = errPipe.fileHandleForReading.readDataToEndOfFile()
        let errMsg = String(data: errData, encoding: .utf8) ?? "ffmpeg failed"
        throw NSError(domain: "personal-voice-speak", code: 4, userInfo: [NSLocalizedDescriptionKey: errMsg])
    }
    try? FileManager.default.removeItem(at: tmpURL)
}

struct Args {
    var list = false
    var authorize = false
    var personalOnly = false
    var voice: String?
    var text: String?
    var out: String?
}

func parseArgs() -> Args {
    var a = Args()
    let args = CommandLine.arguments
    var i = 1
    while i < args.count {
        switch args[i] {
        case "--list", "-l": a.list = true
        case "--authorize", "--auth": a.authorize = true
        case "--personal-only": a.personalOnly = true
        case "--voice", "-v":
            i += 1
            if i < args.count { a.voice = args[i] }
        case "--text", "-t":
            i += 1
            if i < args.count { a.text = args[i] }
        case "--out", "-o":
            i += 1
            if i < args.count { a.out = args[i] }
        default: break
        }
        i += 1
    }
    return a
}

if #available(macOS 14.0, *) {
    let args = parseArgs()
    if args.list || args.authorize {
        Task {
            let auth = await requestAuth()
            fputs("Personal Voice: \(authDescription(auth))\n", stderr)
            if args.authorize {
                fputs("If a dialog appeared, approve it, then re-run your test.\n", stderr)
            }
            if args.list {
                await listVoices(personalOnly: args.personalOnly)
            }
            exit(auth == .authorized ? 0 : 1)
        }
        dispatchMain()
    }
    guard let voiceName = args.voice, let text = args.text, let out = args.out else {
        fputs(
            "Usage:\n"
                + "  swift personal-voice-speak.swift --authorize\n"
                + "  swift personal-voice-speak.swift --list [--personal-only]\n"
                + "  swift personal-voice-speak.swift -v \"Jacob Adkins\" -t \"text\" -o out.wav\n",
            stderr
        )
        exit(1)
    }
    Task {
        let auth = await requestAuth()
        fputs("Authorization: \(authDescription(auth))\n", stderr)
        guard let voice = findVoice(named: voiceName) else {
            fputs("Voice not found: \(voiceName)\nRun: swift personal-voice-speak.swift --list --personal-only\n", stderr)
            exit(2)
        }
        fputs("Using: \(voice.name) (\(voice.identifier))\n", stderr)
        do {
            try synthesizeToWav(text: text, voice: voice, outPath: out)
            fputs("OK \(out)\n", stderr)
            exit(0)
        } catch {
            fputs("Error: \(error)\n", stderr)
            fputs(
                "Tip: say + mysay often works when AVSpeech write fails:\n"
                    + "  DYLD_INSERT_LIBRARIES=./mysay.dylib say -v \"\(voice.name)\" -o /tmp/test.caf \"\(text.prefix(40))...\"\n",
                stderr
            )
            exit(3)
        }
    }
    dispatchMain()
} else {
    fputs("Requires macOS 14+\n", stderr)
    exit(1)
}
