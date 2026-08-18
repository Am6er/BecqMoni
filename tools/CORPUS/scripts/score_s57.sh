#!/bin/sh
# Свести развёртку S57 в одну таблицу. Мерка та же, что у всех: score.py --members --part=.
cd "$(dirname "$0")/../../.." || exit 1
printf '%-22s | %-24s | %-24s\n' 'прогон' 'ПОНЯТНАЯ (81)' 'НЕПОНЯТНАЯ (40)'
printf '%-22s | %6s %7s %6s | %6s %7s %6s\n' '' 'recall' 'фантом' 'невяз' 'recall' 'фантом' 'невяз'
for d in "$@"; do
    line=""
    for part in known unknown; do
        out=$(PYTHONIOENCODING=utf-8 python tools/pie/score.py --mode=spline --members \
              --part=$part --out-dir="tools/pie/$d" 2>/dev/null)
        r=$(printf '%s\n' "$out" | sed -n 's/^итого *[0-9]* *\([0-9]*\)%.*/\1/p')
        f=$(printf '%s\n' "$out" | sed -n 's/^итого *[0-9]* *[0-9]*% *\([0-9]*\).*/\1/p')
        e=$(printf '%s\n' "$out" | sed -n 's/.*model residual медиана \([0-9.]*\).*/\1/p')
        line="$line$(printf '%5s%% %7s %5s%% | ' "${r:--}" "${f:--}" "${e:--}")"
    done
    printf '%-22s | %s\n' "${d#out_s57_}" "$line"
done
