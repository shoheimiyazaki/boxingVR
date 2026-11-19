# 戦略の層の A/B（rosterleague --gameplan ab と --gameplan off --ab on を同じ種で回した 2 つの --matrix）を、層を入れた選手ごとの対の差にする。
# 使い方: py tools/roster-report/gameplan-ab-pair.py <ab_on_SEED.csv と ab_off_SEED.csv のあるフォルダ> <SEED>
# Δ = 層を入れたその選手だけ（相手は層なし）− 全員層なし。score は重みつきの点（docs/BOTS.md「物差し」）。
import csv,collections,math,sys
d=sys.argv[1]; seed=sys.argv[2]
def load(p): return {(r['red'],r['blue'],r['seed']):r for r in csv.DictReader(open(p,encoding='utf-8'))}
on=load(f'{d}/ab_on_{seed}.csv'); off=load(f'{d}/ab_off_{seed}.csv')
per=collections.defaultdict(lambda: collections.defaultdict(list))
for k,a in on.items():
    b=off[k]; x=a['layered']; assert x==b['layered']
    side='red' if a['red']==x else 'blue'; o='blue' if side=='red' else 'red'
    P=per[x]
    P['score'].append(float(a[f'{side}_score'])-float(b[f'{side}_score']))
    P['kd'].append(float(a[f'{side}_knockdowns'])-float(b[f'{side}_knockdowns']))
    P['kdT'].append(float(a[f'{o}_knockdowns'])-float(b[f'{o}_knockdowns']))
    P['brk'].append(float(a[f'{side}_breaks'])-float(b[f'{side}_breaks']))
    P['dmg'].append(float(a[f'{side}_damage'])-float(b[f'{side}_damage']))
    P['dmgT'].append(float(a[f'{o}_damage'])-float(b[f'{o}_damage']))
    P['base'].append(float(b[f'{side}_score']))
m=lambda x:sum(x)/len(x)
se=lambda x: math.sqrt(sum((v-m(x))**2 for v in x)/(len(x)-1)/len(x))
print(f"{'fighter':9} {'base':>6} {'Δscore':>7}±se   ΔKO取  ΔKO受  Δ骨折  Δ与ダメ Δ受ダメ  n")
for f in sorted(per,key=lambda f:-m(per[f]['score'])):
    P=per[f]
    print(f"{f:9} {m(P['base']):6.1f} {m(P['score']):7.1f}±{se(P['score']):4.1f} {m(P['kd']):6.2f} {m(P['kdT']):6.2f} {m(P['brk']):6.2f} {m(P['dmg']):7.1f} {m(P['dmgT']):7.1f} {len(P['score'])}")
