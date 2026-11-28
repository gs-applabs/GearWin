#!/usr/bin/env python3
"""Read-only tracked-source inventory. Pattern hits are candidates, not confirmed bugs."""
import csv, hashlib, json, pathlib, re, subprocess, sys
from collections import Counter, defaultdict

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
counts=Counter(); files=[]; hits=[]; code={}
for rel in filter(None,paths):
    p=root/rel
    if not p.is_file():
        files.append([rel,"missing",0,"",0]); continue
    data=p.read_bytes()
    ext=p.suffix.lower()
    lines=0
    if ext in suffixes:
        source=data.decode("utf-8-sig",errors="replace")
        if ext in {".cs",".xaml",".ps1",".py",".iss",".sh"}:
            code[rel]=source.splitlines()
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

# Cross-reference and exact-window duplication candidates. These are lexical
# evidence, not a C#/XAML semantic model. Never emit source contents.
service_names=set()
for rel,lines in code.items():
    if rel.startswith("wpf/OptimizerWpf/Services/") and rel.endswith(".cs"):
        for line in lines:
            m=re.search(r"\b(?:class|struct|record)\s+([A-Za-z_]\w*)",line)
            if m and not line.lstrip().startswith("//"):
                service_names.add(m.group(1))
service_ref=re.compile(r"\b("+"|".join(re.escape(n) for n in sorted(service_names))+r")\s*\.\s*([A-Za-z_]\w*)\s*\(") if service_names else None
watch=re.compile(r"\b(?:InstallInBackground|RunPowerShellScriptDetached|DirSize|ScanBrowserCacheSizeAsync|BrowserCacheSize|DisplayIcon|UserAssist|MSFT_PhysicalDisk|FreeBackgroundMemoryAsync)\b")
event_ref=re.compile(r'\b(Click|Checked|Unchecked|SelectionChanged|TextChanged|Loaded|Unloaded|KeyDown|PreviewKeyDown|MouseDown|MouseUp|MouseDoubleClick|PreviewMouseWheel|ValueChanged|Closing|Closed)\s*=\s*"([A-Za-z_]\w*)"')
call_rows=[]; watch_rows=[]; event_rows=[]
fingerprints=defaultdict(list)
WINDOW=12
for rel,lines in code.items():
    if service_ref and rel.endswith(".cs"):
        for i,line in enumerate(lines,1):
            if line.lstrip().startswith("//"):continue
            for m in service_ref.finditer(line):
                call_rows.append((rel,i,m.group(1),m.group(2)))
    for i,line in enumerate(lines,1):
        for m in watch.finditer(line):
            watch_rows.append((rel,i,m.group(0)))
        if rel.endswith(".xaml"):
            for m in event_ref.finditer(line):
                handler=m.group(2)
                counterpart=rel+".cs"
                local=any(re.search(r"\b"+re.escape(handler)+r"\s*\(",l) for l in code.get(counterpart,[]))
                event_rows.append((rel,i,m.group(1),handler,counterpart,"candidate-local" if local else "review-inherited-or-missing"))
    # Compare exact normalized source windows, not tests or generated code.
    if not rel.endswith(".cs") or not rel.startswith(("wpf/OptimizerWpf/","migration/GearWin.Migration.Core/","migration/GearWin.Migration.Cli/")):
        continue
    compact=[(i,line.strip()) for i,line in enumerate(lines,1) if line.strip() and not line.lstrip().startswith(("//","/*","*","*/"))]
    for j in range(len(compact)-WINDOW+1):
        segment=compact[j:j+WINDOW]
        body="\n".join(line for _,line in segment)
        if len(body)<180:continue
        fingerprint=hashlib.sha256(body.encode("utf-8")).hexdigest()
        fingerprints[fingerprint].append((rel,segment[0][0],segment[-1][0]))
duplicate_rows=[]; duplicate_groups=0
for fingerprint,locations in sorted(fingerprints.items()):
    # Retain only groups with two disjoint windows; overlapping windows in
    # one file may be coincidental repetitions of a shorter pattern.
    first=locations[0]
    if not any(loc[0]!=first[0] or loc[1]>first[2] for loc in locations[1:]):continue
    duplicate_groups+=1
    if duplicate_groups<=2000:
        for rel,start,end in locations[:100]:
            duplicate_rows.append((fingerprint,rel,start,end,len(locations)))
for name,header,rows in [
    ("service-call-candidates.csv",["path","line","type","member"],call_rows),
    ("review-symbol-candidates.csv",["path","line","symbol"],watch_rows),
    ("xaml-handler-candidates.csv",["path","line","event","handler","paired_code","lexical_status"],event_rows),
    ("duplicate-window-candidates.csv",["fingerprint","path","first_line","last_line","group_occurrences"],duplicate_rows)]:
    with (out/name).open("w",encoding="utf-8",newline="") as f:
        w=csv.writer(f);w.writerow(header);w.writerows(rows)

summary={"commit":subprocess.check_output(["git","-C",str(root),"rev-parse","HEAD"],text=True).strip(),"tracked_paths":len(files),"scanned_source_files":sum(row[4]>0 for row in files),"scanned_source_lines":sum(row[4] for row in files),"missing_paths":[row[0] for row in files if row[1]=="missing"],"candidate_counts":dict(counts),"service_call_candidates":len(call_rows),"review_symbol_candidates":len(watch_rows),"xaml_handler_candidates":len(event_rows),"duplicate_window_groups":duplicate_groups,"duplicate_window_report_group_limit":2000,"limits":"Lexical inventory only; calls and XAML handlers may be false positives/negatives; duplicate windows are not semantic duplicates; excludes unsupported extensions and does not establish runtime reachability, semantic correctness or test coverage."}
(out/"summary.json").write_text(json.dumps(summary,indent=2),encoding="utf-8")
print(json.dumps(summary,indent=2))
if summary["missing_paths"]:sys.exit(1)
