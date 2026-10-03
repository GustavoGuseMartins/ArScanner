import json, re
from pathlib import Path

def parse(text):
    tokens = iter(re.findall(r'"(?:\\.|[^"\\])*"|[()]|[^\s()]+', text))
    def node():
        out = []
        for t in tokens:
            if t == ')': return out
            out.append(node() if t == '(' else json.loads(t) if t.startswith('"') else t)
        return out
    next(tokens)
    return node()

def children(node, key):
    return [x for x in node if isinstance(x, list) and x and x[0] == key]

def field(node, key):
    rows = children(node, key)
    return rows[0][1] if rows and len(rows[0]) > 1 else ''

for filename in ('tcc.net', 'tccvis.net'):
    doc = parse(Path(filename).read_text(encoding='utf-8'))
    lines = [filename]
    for comp in children(children(doc, 'components')[0], 'comp'):
        lines.append(f"{field(comp, 'ref')}: {field(comp, 'value')}")
    for net in children(children(doc, 'nets')[0], 'net'):
        lines.append(field(net, 'name') + ': ' + ', '.join(
            f"{field(n, 'ref')}.{field(n, 'pin')}[{field(n, 'pinfunction')}]"
            for n in children(net, 'node')))
    print('\n'.join(lines))
