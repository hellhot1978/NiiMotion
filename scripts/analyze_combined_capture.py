import json, math, sys
from pathlib import Path

def mag(v): return math.sqrt(sum(float(v.get(k, 0)) ** 2 for k in ("X", "Y", "Z")))
def corr(a, b):
    if len(a) < 3: return 0.0
    am, bm = sum(a)/len(a), sum(b)/len(b)
    num = sum((x-am)*(y-bm) for x,y in zip(a,b))
    den = math.sqrt(sum((x-am)**2 for x in a)*sum((y-bm)**2 for y in b))
    return num/den if den else 0.0

def bins(path, name, extractor, width=100):
    out = {}
    with open(path/name, encoding="utf-8") as f:
        for line in f:
            row=json.loads(line); t=int(row.get("elapsedMs",0))//width
            out.setdefault(t,[]).append(extractor(row))
    return {k:sum(v)/len(v) for k,v in out.items()}

def analyze(folder):
    joy=bins(folder,"joycons.jsonl",lambda r:mag(r["sample"]["AngularVelocityDps"]))
    phone=bins(folder,"phone.jsonl",lambda r:mag(r["sample"]["AccelerationMps2"]))
    common=sorted(set(joy)&set(phone)); best=(-99,0)
    for lag in range(-10,11):
        pairs=[(joy[t],phone[t+lag]) for t in common if t+lag in phone]
        c=corr([x for x,_ in pairs],[y for _,y in pairs])
        if c>best[0]: best=(c,lag)
    jvals=sorted(joy.values()); pvals=sorted(phone.values())
    return {"session":folder.name,"durationSeconds":round(max(common)*.1,1),"overlapBins":len(common),
            "bestCorrelation":round(best[0],3),"phoneLagMs":best[1]*100,
            "joyP95":round(jvals[int(len(jvals)*.95)],2),"phoneAccelP95":round(pvals[int(len(pvals)*.95)],2)}

root=Path(sys.argv[1] if len(sys.argv)>1 else r"C:\NiirMotion\data\user-gait")
sessions=[]
for label in ("natural","stop"):
    candidates=sorted((p for p in root.iterdir() if p.name.endswith(label) and (p/"phone.jsonl").exists()),key=lambda p:p.stat().st_mtime,reverse=True)
    if candidates:sessions.append(analyze(candidates[0]))
print(json.dumps(sessions,indent=2,ensure_ascii=False))
