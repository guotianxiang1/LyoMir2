import sys, io

def remove_members(path, names):
    with io.open(path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    for name in names:
        # find signature line: contains the identifier as a whole-ish token
        sig = None
        for i, ln in enumerate(lines):
            s = ln.strip()
            if (('BuildSm' in name and (name + '(') in ln) or
                (name == 'Sm966Body' and 'Sm966Body' in ln and '=' in ln and 'Clone' not in ln)):
                sig = i
                break
        if sig is None:
            print('NOT FOUND:', name)
            continue
        # walk up over contiguous // comment lines
        start = sig
        j = sig - 1
        while j >= 0 and lines[j].strip().startswith('//'):
            start = j
            j -= 1
        # walk forward: brace match if we see '{' before ';', else stop at first ';'
        end = None
        brace = 0
        seen_brace = False
        k = sig
        while k < len(lines):
            for ch in lines[k]:
                if ch == '{':
                    brace += 1; seen_brace = True
                elif ch == '}':
                    brace -= 1
            if seen_brace:
                if brace == 0:
                    end = k; break
            else:
                if ';' in lines[k]:
                    end = k; break
            k += 1
        if end is None:
            print('NO END:', name); continue
        # consume one trailing blank line
        e = end
        if e + 1 < len(lines) and lines[e+1].strip() == '':
            e += 1
        del lines[start:e+1]
        print('removed %s  (lines %d..%d)' % (name, start+1, end+1))
    with io.open(path, 'w', encoding='utf-8') as f:
        f.writelines(lines)

remove_members('GameSvr/Actors/TBaseObject.SmIdent_Sm1.cs', ['Sm966Body', 'BuildSm966'])
remove_members('GameSvr/Actors/TBaseObject.SmIdent_Sm2.cs',
    ['BuildSm689','BuildSm951','BuildSm959','BuildSm965','BuildSm1201',
     'BuildSm1250','BuildSm1251','BuildSm1252','BuildSm1253','BuildSm1254','BuildSm1255','BuildSm1256'])
