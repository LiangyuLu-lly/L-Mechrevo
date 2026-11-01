import json, sys

def load(p):
    with open(p, encoding='utf-8') as f:
        return json.load(f)

def walk(node, path, out):
    if node is None:
        out[path] = '<NOT EXIST>'
        return
    if isinstance(node, dict):
        if '.values' in node:
            for name, v in sorted(node['.values'].items()):
                out[path + ' :: ' + name] = v
        if '.subkeys' in node:
            for sub, child in sorted(node['.subkeys'].items()):
                walk(child, path + '\\' + sub, out)
    else:
        out[path] = node

before = load(sys.argv[1])
after = load(sys.argv[2])

b = {}
a = {}
for root, node in before.items():
    walk(node, root, b)
for root, node in after.items():
    walk(node, root, a)

keys = sorted(set(b) | set(a))
changed = 0
for k in keys:
    bv = b.get(k)
    av = a.get(k)
    if bv != av:
        changed += 1
        print('CHANGED:', k)
        print('   before:', json.dumps(bv, ensure_ascii=False)[:300])
        print('   after :', json.dumps(av, ensure_ascii=False)[:300])
print('--- total changed entries:', changed)
