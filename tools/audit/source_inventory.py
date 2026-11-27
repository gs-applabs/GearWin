#!/usr/bin/env python3
"""Read-only tracked-source inventory. Pattern hits are candidates, not confirmed bugs."""
import csv, hashlib, json, pathlib, re, subprocess, sys
from collections import Counter

root=pathlib.Path(subprocess.check_output(["git","rev-parse","--show-toplevel"],text=True).strip())
out=pathlib.Path(sys.argv[1]).resolve()
out.mkdir(parents=True,exist_ok=True)
paths=subprocess.check_output(["git","-C",str(root),"ls-files","-z"]).decode("utf-8").split("\0")
rules={
 "registry_mutation":r"\b(?:SetValue|DeleteValue|DeleteSubKey(?:Tree)?|CreateSubKey|RegSet|SetRegSafe)\s*\(|\b(?:reg(?:\.exe)?\s+(?:add|delete|import)|Set-ItemProperty|Remove-ItemProperty)\b",
 "file_mutation":r"\b(?:File|Directory)\.(?:Delete|Move|Copy|Create|WriteAll\w*|AppendAll\w*)\s*\(|\b(?:Remove-Item|Clear-Content|Set-Content|Out-File)\b",
 "subprocess":r"\b(?:Process\.Start|ProcessStartInfo|WaitForExit(?:Async)?|Kill)\b",
 "timer_lifecycle":r"\b(?:DispatcherTimer|PeriodicTimer|System\.Timers\.Timer|System\.Threading\.Timer|Task\.Delay|Loaded\s*\+=|Unloaded\s*\+=)\b",
 "input_route":r"\b(?:RegisterHotKey|UnregisterHotKey|InputGestureText|KeyBinding|PreviewKeyDown|SelectTab|OpenDestination|MenuLeaf|SidebarShortcut)\b",
 "ui_command":r"\b(?:Click|Checked|Unchecked|SelectionChanged|Command|KeyDown)\s*=",
 "error_handling":r"\bcatch\s*(?:\{|\()|\basync\s+void\b",
 "privilege":r"\b(?:requireAdministrator|runas|RegistryHive\.LocalMachine|Registry\.LocalMachine|IsAdministrator|RequiresAdmin)\b",
 "test_declaration":r"\[\s*(?:Fact|Theory)(?:\s*\(|\s*\])|^\s*def\s+test_",
 "opt_in":r"GEARWIN_[A-Z0-9_]+",
 "dependency":r"<(?:PackageReference|ProjectReference|TargetFramework)\b",
 "status":r"\bStatusService\.(?:SetBusy|SetIdle|BeginScope)\b",
}
compiled={k:re.compile(v,re.I) for k,v in rules.items()}
suffixes={".cs",".xaml",".ps1",".py",".iss",".csproj",".props",".targets",".yml",".yaml",".json",".sh",".bat",".cmd",".xml",".config"}
counts=Counter(); files=[]; hits=[]
for rel in filter(None,paths):
    p=root/rel
    if not p.is_file():
        files.append([rel,"missing",0,"",0]); continue
    data=p.read_bytes()
    ext=p.suffix.lower()
    lines=0
    if ext in suffixes:
        source=data.decode("utf-8-sig",errors="replace")
        for i,line in enumerate(source.splitlines(),1):
            lines=i
            for key,pattern in compiled.items():
                if pattern.search(line):
                    counts[key]+=1
                    # Locations only, never copy potential secret values into audit reports.
                    hits.append([rel,i,key])
    files.append([rel,ext,len(data),hashlib.sha256(data).hexdigest(),lines])
for name,header,rows in [("files.csv",["path","type","bytes","sha256","scanned_lines"],files),("candidates.csv",["path","line","category"],hits)]:
    with (out/name).open("w",encoding="utf-8",newline="") as f:
        w=csv.writer(f);w.writerow(header);w.writerows(rows)
summary={"commit":subprocess.check_output(["git","-C",str(root),"rev-parse","HEAD"],text=True).strip(),"tracked_paths":len(files),"scanned_source_files":sum(row[4]>0 for row in files),"scanned_source_lines":sum(row[4] for row in files),"missing_paths":[row[0] for row in files if row[1]=="missing"],"candidate_counts":dict(counts),"limits":"Lexical inventory only; excludes unsupported extensions and does not establish runtime reachability, semantic correctness or test coverage."}
(out/"summary.json").write_text(json.dumps(summary,indent=2),encoding="utf-8")
print(json.dumps(summary,indent=2))
if summary["missing_paths"]:sys.exit(1)
