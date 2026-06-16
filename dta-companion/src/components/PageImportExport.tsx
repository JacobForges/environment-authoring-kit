import React, { useState } from 'react';
import { 
  Upload, FileJson, CheckCircle2, AlertTriangle, Cpu, Download, 
  Trash2, Layers, ShieldCheck, RefreshCw, FileText
} from 'lucide-react';

interface PageImportExportProps {
  onImportSuccess: (agentDetails: { displayName: string; level: number; xp: number }) => void;
}

export default function PageImportExport({ onImportSuccess }: PageImportExportProps) {
  const [isDragOver, setIsDragOver] = useState(false);
  const [importedPackName, setImportedPackName] = useState<string | null>(null);
  const [isValidating, setIsValidating] = useState(false);
  const [parsedFiles, setParsedFiles] = useState<any[]>([]);

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  };

  const handleDragLeave = () => {
    setIsDragOver(false);
  };

  const triggerMockImport = (filename: string) => {
    setIsValidating(true);
    setImportedPackName(filename);
    setParsedFiles([]);

    setTimeout(() => {
      setIsValidating(false);
      setParsedFiles([
        { name: 'vault_manifest.json', type: 'JSON', size: '1.2 KB', status: 'VERIFIED', comment: 'Contains agent schema structures' },
        { name: 'training_catalog', type: 'LEDGER', size: '24 KB', status: 'VERIFIED', comment: 'Contains 31 gameplay cycles' },
        { name: 'episode_101_battle_obs.jsonl', type: 'JSONL', size: '112 KB', status: 'VERIFIED', comment: '96 Feature dimensions check' },
        { name: 'episode_102_battle_obs.jsonl', type: 'JSONL', size: '144 KB', status: 'VERIFIED', comment: '96 Feature dimensions check' },
        { name: 'competition_gameplay.onnx', type: 'ONNX', size: '3.4 MB', status: 'VERIFIED', comment: 'Sentis raw model weights' }
      ]);
      
      onImportSuccess({
        displayName: "SensiScout Optimizer",
        level: 8,
        xp: 2900
      });
    }, 1500);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
    const files = e.dataTransfer.files;
    if (files.length > 0) {
      triggerMockImport(files[0].name);
    }
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (files && files.length > 0) {
      triggerMockImport(files[0].name);
    }
  };

  const handleClearImport = () => {
    setImportedPackName(null);
    setParsedFiles([]);
  };

  const handleExportPack = () => {
    // Generate a mock download of exported-agent-pack.hubai
    alert("Compiling current active weights and packaging manifest vectors into scout_optimizer_checkpoint.hubai... Downloading zip bundle.");
    
    // Simulating file creation
    const textBlob = "MOCK_HUBAI_DATA_BINARY_REBOUND";
    const dataStr = "data:text/plain;charset=utf-8," + encodeURIComponent(textBlob);
    const downloadAnchor = document.createElement('a');
    downloadAnchor.setAttribute("href", dataStr);
    downloadAnchor.setAttribute("download", "scout_optimizer_checkpoint.hubai");
    document.body.appendChild(downloadAnchor);
    downloadAnchor.click();
    downloadAnchor.remove();
  };

  return (
    <div className="space-y-8">
      {/* Page Title */}
      <div className="border-b border-border-subtle pb-6 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <span className="text-xs font-mono text-accent-cyan font-bold uppercase tracking-wider">binary vault serialization</span>
          <h2 className="text-3xl font-display font-semibold text-white mt-1">Import & Export Agent Vault</h2>
          <p className="text-text-secondary text-sm font-sans mt-1">
            Re-package behavior cloning nodes. Load <code className="text-accent-cyan font-mono bg-bg-deep px-1.5 py-0.5 rounded text-xs border border-border-subtle">.hubai</code> archives containing original ONNX policies and observations maps.
          </p>
        </div>
      </div>

      <div className="grid lg:grid-cols-12 gap-8">
        
        {/* Left Column: Parsed results (Span 7) */}
        <div className="lg:col-span-7 space-y-6">
          
          <div className="bg-bg-panel p-6 rounded-2xl border border-border-subtle space-y-6">
            <h3 className="text-sm font-display font-bold text-white uppercase tracking-wider flex items-center gap-2">
              <Layers className="w-4 h-4 text-accent-cyan" />
              1. Import Agent Pack (.hubai)
            </h3>

            {/* Dropzone */}
            {!importedPackName ? (
              <div
                onDragOver={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
                className={`border-2 border-dashed rounded-xl p-8 text-center transition cursor-pointer flex flex-col items-center justify-center min-h-[220px] ${
                  isDragOver 
                    ? 'border-accent-cyan bg-accent-cyan/5' 
                    : 'border-border-subtle hover:border-accent-cyan/15 hover:bg-bg-deep/20'
                }`}
                onClick={() => document.getElementById('hubai-upload')?.click()}
              >
                <div className="w-12 h-12 rounded-full bg-bg-deep border border-border-subtle flex items-center justify-center mb-4">
                  <Upload className="w-6 h-6 text-text-secondary group-hover:text-white transition-colors" />
                </div>
                
                <div>
                  <h4 className="text-white text-sm font-semibold">Drag & drop files or click to upload</h4>
                  <p className="text-text-secondary text-xs font-sans mt-1">
                    Select a <code className="text-accent-cyan font-mono bg-bg-deep px-1.5 py-0.2 rounded border border-border-subtle text-[11px]">.hubai</code> or compressed zip file compiled from Unity Agent Vault menu.
                  </p>
                </div>

                <input
                  id="hubai-upload"
                  type="file"
                  accept=".zip,.hubai"
                  onChange={handleFileChange}
                  className="hidden"
                />
              </div>
            ) : (
              <div className="p-5 bg-bg-deep border border-border-subtle rounded-xl space-y-4">
                <div className="flex justify-between items-center text-xs font-mono">
                  <div className="flex items-center gap-2 text-white font-bold">
                    <FileJson className="w-4 h-4 text-accent-cyan" />
                    <span>{importedPackName}</span>
                  </div>
                  <button 
                    onClick={handleClearImport}
                    className="p-1 hover:bg-bg-panel rounded text-accent-rose transition"
                    title="Remove pack"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>

                {isValidating ? (
                  <div className="flex items-center gap-2 text-xs font-mono text-accent-violet animate-pulse">
                    <RefreshCw className="w-4 h-4 animate-spin text-accent-violet" />
                    <span>VERIFYING CRYPTOGRAPHIC VAULT MANIFEST SCHEMAS...</span>
                  </div>
                ) : (
                  <div className="flex items-center gap-2 text-xs font-mono text-accent-emerald">
                    <ShieldCheck className="w-4 h-4 text-accent-emerald" />
                    <span>VAULT PARSE VERIFIED · ALL CHECKSUMS INTEGRAL</span>
                  </div>
                )}
              </div>
            )}

            {/* Render parsed files list once loaded */}
            {parsedFiles.length > 0 && (
              <div className="space-y-4 pt-2">
                <span className="text-[10px] font-mono text-text-muted uppercase tracking-wider block">Archive contents map disclosure</span>
                
                <div className="space-y-2 max-h-56 overflow-y-auto">
                  {parsedFiles.map((file, i) => (
                    <div key={i} className="p-3 bg-bg-deep rounded-lg border border-border-subtle flex items-center justify-between gap-4">
                      <div className="flex items-center gap-3">
                        <FileText className="w-4 h-4 text-accent-cyan shrink-0" />
                        <div>
                          <span className="text-xs font-mono text-white block">{file.name}</span>
                          <span className="text-[9.5px] text-text-secondary font-mono leading-none">{file.comment}</span>
                        </div>
                      </div>

                      <div className="flex items-center gap-3 text-[10px] font-mono text-text-secondary shrink-0">
                        <span>{file.size}</span>
                        <span className="px-1.5 py-0.2 bg-accent-emerald/10 border border-accent-emerald/20 text-accent-emerald rounded font-bold text-[9px]">
                          {file.status}
                        </span>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}

          </div>

        </div>

        {/* Right Column: Exporting bundles (Span 5) */}
        <div className="lg:col-span-5 space-y-6">
          
          <div className="bg-bg-panel p-6 rounded-2xl border border-border-subtle space-y-4">
            <h3 className="text-sm font-display font-bold text-white uppercase tracking-wider flex items-center gap-2">
              <Download className="w-4 h-4 text-accent-violet" />
              2. Export Current Agent Vault
            </h3>
            
            <p className="text-text-secondary text-xs font-sans leading-relaxed">
              Have you finished coaching or training behavior clone weights inside active labs? Re-package your customized workspace checkpoints version into a cohesive archive package (`.hubai`) designed for direct transfer back to local Unity folders.
            </p>

            <div className="p-4 bg-bg-deep rounded-xl border border-border-subtle space-y-2.5 text-xs font-mono">
              <div className="flex justify-between">
                <span className="text-text-muted">Target Agent:</span>
                <span className="text-white">usr_agent_scout_01</span>
              </div>
              <div className="flex justify-between">
                <span className="text-text-muted">Active Focus Activity:</span>
                <span className="text-accent-cyan font-bold">BATTLE MODE</span>
              </div>
              <div className="flex justify-between">
                <span className="text-text-muted">Revision Version:</span>
                <span className="text-white">v7 checkpoint node</span>
              </div>
            </div>

            <button
              onClick={handleExportPack}
              className="w-full py-4 bg-accent-violet hover:bg-accent-violet/90 text-[#070B14] font-bold text-xs rounded-xl cursor-pointer transition flex items-center justify-center gap-2 shadow-lg shadow-accent-violet/15"
            >
              <Cpu className="w-4 h-4" />
              Compile & Export .hubai Pack
            </button>
          </div>

          <div className="p-4 bg-bg-panel rounded-2xl border border-border-subtle flex items-start gap-3">
            <AlertTriangle className="w-4 h-4 text-accent-amber shrink-0 mt-0.5" />
            <div className="text-xs text-text-secondary leading-relaxed">
              <strong className="text-white">Interoperability Alert:</strong> Do NOT manually modify the binary parameters inside zip contents as structural metadata shifts will fail quality checking schema guards on reloading vaults!
            </div>
          </div>

        </div>

      </div>
    </div>
  );
}
