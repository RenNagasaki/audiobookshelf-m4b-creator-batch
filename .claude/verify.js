#!/usr/bin/env node
// .claude/verify.js — das EINE Prüfkommando dieses Projekts.
//
// Aufruf:  node .claude/verify.js      (keine Argumente, aus dem Repo-Wurzelverzeichnis)
//
// Vertrag (global, siehe CLAUDE.md, 'Verify-Konvention'):
//   exit 0        = Build UND Tests grün
//   exit != 0     = kaputt; stdout/stderr enthalten den ersten echten Fehler
//
// Wozu: Subagenten (auch guenstige Modelle) pruefen ihre eigene Arbeit, ohne
// die Toolchain zu kennen. Deshalb NIE mit exit 0 aussteigen, wenn nichts
// geprueft wurde — 'nicht konfiguriert' ist ein FEHLER, kein Erfolg.
//
// Node und nicht PowerShell, damit dieselbe Datei auf Windows und macOS laeuft:
// node ist durch die Hooks ohnehin Pflicht, pwsh waere ein zweites Laufzeitsystem.
//
// Projekt zielt auf net10.0; das globale dotnet hat evtl. nur SDK 9. Liegt ein
// benutzerlokales SDK unter ~/.dotnet, wird das bevorzugt.

const { spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");

// Ein Aufruf. Die Ausgabe geht unveraendert durch, damit der erste echte
// Fehler im Klartext sichtbar bleibt statt in einem Puffer zu verschwinden.
function run(cmd, args) {
  // Windows loest npm/npx/yarn/pnpm nur ueber ihre .cmd-Wrapper auf.
  const win = process.platform === "win32";
  const bin = win && /^(npm|npx|yarn|pnpm)$/.test(cmd) ? `${cmd}.cmd` : cmd;
  const r = spawnSync(bin, args, { stdio: "inherit", cwd: root });
  if (r.error) {
    console.error(`verify: '${cmd}' nicht ausfuehrbar — ${r.error.message}`);
    return 127;
  }
  return r.status === null ? 1 : r.status;
}

// Ein Schritt, der gruen sein MUSS: beim ersten roten ist Schluss.
function step(cmd, args) {
  const code = run(cmd, args);
  if (code !== 0) process.exit(code);
}

function fail(msg) {
  console.error(`verify: ${msg}`);
  process.exit(2);
}

const os = require("os");
const localRoot = path.join(os.homedir(), ".dotnet");
const localExe = path.join(localRoot, process.platform === "win32" ? "dotnet.exe" : "dotnet");
const dotnet = fs.existsSync(localExe) ? localExe : "dotnet";
if (dotnet === localExe) process.env.DOTNET_ROOT = localRoot;

step(dotnet, ["build", "--nologo", "-v", "quiet"]);
step(dotnet, ["test", "--nologo", "-v", "quiet"]);
process.exit(0);
