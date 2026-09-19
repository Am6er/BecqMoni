# -*- coding: utf-8 -*-
"""П113: пересобрать `recompute.csv` первого прогона `MatrixRecomputeProbe` в правильный csv.

Первая сборка пробы писала клейма БЕЗ кавычек, а клейма несут `;` (разделитель): столбцы
разъезжались. Проба починена (поля с `;` — в кавычках по RFC 4180); этот скрипт приводит
уже записанный файл к тому же виду — по известной структуре строки:
  16 полей, из них matrix_stamp = ровно один `;` внутри, curve_stamp = много, rmx_path — последнее.

    python fix_recompute_csv.py <in.csv> <out.csv>
"""
import csv
import sys

HEADER = ("device;curve;guid;matrix_seconds;matrix_cpu_seconds;matrix_nodes;matrix_hist_spent;"
          "matrix_noise_weighted_pct;matrix_stamp;matrix_file_bytes;curve_seconds;curve_points;"
          "curve_emin;curve_emax;curve_stamp;rmx_path").split(";")


def main(src, dst):
    with open(src, encoding="utf-8") as f:
        lines = [l.rstrip("\r\n") for l in f if l.strip()]
    assert lines[0].split(";") == HEADER, lines[0]
    rows = []
    for line in lines[1:]:
        c = line.split(";")
        head = c[:8]
        matrix_stamp = c[8] + ";" + c[9]
        bytes_ = c[10]
        curve_seconds, curve_points, curve_emin, curve_emax = c[11:15]
        rmx_path = c[-1]
        curve_stamp = ";".join(c[15:-1])
        row = head + [matrix_stamp, bytes_, curve_seconds, curve_points, curve_emin, curve_emax,
                      curve_stamp, rmx_path]
        assert len(row) == 16
        assert matrix_stamp.startswith("phys=") and curve_stamp.startswith("phys=")
        assert rmx_path.endswith(".rmx")
        rows.append(row)
    with open(dst, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, delimiter=";", quoting=csv.QUOTE_MINIMAL, lineterminator="\n")
        w.writerow(HEADER)
        w.writerows(rows)
    print("строк: %d -> %s" % (len(rows), dst))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
