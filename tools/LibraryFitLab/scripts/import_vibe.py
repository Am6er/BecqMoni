# -*- coding: utf-8 -*-
"""Перенести отобранные спектры spectravibe-toolkit в библиотеку спектров.

Оригиналы кладутся рядом со всем остальным, в отдельную папку — чтобы
build_corpus.py собирал их тем же путём, что и всё прочее, и чтобы источник
корпуса оставался одним местом.
"""
import os
import shutil

SRC = os.environ.get('SVT_ROOT') or os.path.join(
    os.path.dirname(os.path.abspath(__file__)), 'svt', 'detectors')
DEST = r'C:\Users\moroz\YandexDisk\Спектры\SpectraVibe'

KIT = os.path.join(SRC, 'Gamma-1S', 'reference_spectra', 'reference_kits_becqmoni')
HANDY_HPGE = os.path.join(SRC, 'Handy_HPGe', 'reference_spectra', 'becqmoni',
                          'Work', 'Handy', 'Handy(HPGe)', 'Spe')
HANDY_HPGE_BG = os.path.join(SRC, 'Handy_HPGe', 'reference_spectra', 'background',
                             'becqmoni', 'Work', 'Handy', 'Handy(HPGe)', 'Spe')
GP = os.path.join(SRC, 'GP_HPGe20', 'reference_spectra', 'becqmoni',
                  'Work', 'GP', 'HPGe(20_)', 'Spe')
GP_BG = os.path.join(SRC, 'GP_HPGe20', 'reference_spectra', 'background',
                     'becqmoni', 'Work', 'GP', 'HPGe(20_)', 'Spe', 'Background')
LABR = os.path.join(SRC, 'Handy_LaBr', 'reference_spectra', 'becqmoni',
                    'Work', 'Handy', 'Handy(LaBr)', 'Spe')
LABR_BG = os.path.join(SRC, 'Handy_LaBr', 'reference_spectra', 'background',
                       'becqmoni', 'Work', 'Handy', 'Handy(LaBr)', 'Spe', 'Background')
TECD = os.path.join(SRC, 'Simple_TeCd', 'reference_spectra', 'becqmoni',
                    'Work', 'Simple', 'TeCd(Demo)', 'Spe')

# (подпапка назначения, исходный путь, имя в библиотеке)
FILES = [
    # --- Гамма-1С: один детектор, аттестованные источники, пять геометрий ---
    ('Gamma-1S', KIT + r'\Denta_120mL\Th-232\sample_Th232_420-7-17_Дента-120мл_0cm.xml',
     'Th232 Дента-120мл.xml'),
    ('Gamma-1S', KIT + r'\Marinelli_1L\Th-232\Th232_420-7-17_Маринелли_0cm.xml',
     'Th232 Маринелли-1л.xml'),
    ('Gamma-1S', KIT + r'\Petri_60mL\Th-232\sample_Th232_420-7-17_Петри-60мл_0cm.xml',
     'Th232 Петри-60мл.xml'),
    ('Gamma-1S', KIT + r'\Denta_120mL\Ra-226\sample_Ra226_420-7-18_Дента-120мл_0cm.xml',
     'Ra226 Дента-120мл.xml'),
    ('Gamma-1S', KIT + r'\Petri_60mL\Ra-226\sample_Ra226_420-7-18_Петри-60мл_0cm.xml',
     'Ra226 Петри-60мл.xml'),
    ('Gamma-1S', KIT + r'\Denta_120mL\K-40\sample_K40_420-7-20_Дента-120мл_0cm.xml',
     'K40 Дента-120мл.xml'),
    ('Gamma-1S', KIT + r'\Point_25cm\Th-228\sample_Th-228 №309_Точечная-25см_25cm.xml',
     'Th228 точечный 25см.xml'),
    ('Gamma-1S', KIT + r'\Point_5cm\Th-228\sample_Th-228 #SRC-07_Точечная-5см_5cm.xml',
     'Th228 точечный 5см.xml'),
    ('Gamma-1S', KIT + r'\Point_25cm\Eu-152\sample_Eu-152 #SRC-07_Точечная-25см_25cm.xml',
     'Eu152 точечный 25см.xml'),
    ('Gamma-1S', KIT + r'\Point_5cm\Eu-152\sample_Eu-152 #SRC-07_Точечная-5см_5cm.xml',
     'Eu152 точечный 5см.xml'),
    ('Gamma-1S', KIT + r'\Point_25cm\Co-60\sample_Co-60 #SRC-07_Точечная-25см_25cm.xml',
     'Co60 точечный 25см.xml'),
    ('Gamma-1S', KIT + r'\Point_25cm\Ba-133\sample_Ba-133 #SRC-07_Точечная-25см_25cm.xml',
     'Ba133 точечный 25см.xml'),
    # --- HPGe GMX ---
    ('HPGe GMX', HANDY_HPGE + r'\Th-232  17 kBq.xml', 'Th232 17 кБк.xml'),
    ('HPGe GMX', HANDY_HPGE + r'\Th-228  68 kBq.xml', 'Th228 68 кБк.xml'),
    ('HPGe GMX', HANDY_HPGE + r'\Eu-152  244 kBq.xml', 'Eu152 244 кБк.xml'),
    ('HPGe GMX', HANDY_HPGE_BG + r'\Bckg-1500 keV.xml', 'Фон.xml'),
    # --- HPGe GEM20, маринелли ---
    ('HPGe GEM20', GP + r'\Marinelli\m_th16.xml', 'Th232 маринелли.xml'),
    ('HPGe GEM20', GP + r'\Marinelli\m_ra16.xml', 'Ra226 маринелли.xml'),
    ('HPGe GEM20', GP + r'\Marinelli\m_k16.xml', 'K40 маринелли.xml'),
    # Бланк той же геометрии, что и три образца выше: маринелли с
    # дистиллированной водой, 5 часов набора. Вшитый фон у этих файлов
    # апстрим убрал (он был подобран автоматически и нёс полином 5-й
    # степени), поэтому пара задаётся явно в corpus_def.
    ('HPGe GEM20', GP_BG + r'\Bckg_5.xml', 'Фон маринелли (дист. вода).xml'),
    # калибровка 5-й степени — фикстура к правке ChannelToEnergy, не член корпуса
    ('HPGe GEM20', GP + r'\Point25\Y88-SRC-05-25cm.xml', 'Y88 25см (полином 5й степени).xml'),
    # --- LaBr3 BrilLanCe 380 ---
    ('LaBr3 BrilLanCe', LABR + r'\Th228_#SRC-17_24sm.xml', 'Th228 24см.xml'),
    ('LaBr3 BrilLanCe', LABR + r'\Eu152_#SRC-13_24sm.xml', 'Eu152 24см.xml'),
    ('LaBr3 BrilLanCe', LABR_BG + r'\Background_1.xml', 'Фон.xml'),
    # --- CZT Te(Cd) ---
    ('CZT TeCd', TECD + r'\AmCeCsCoY.xml', 'Смесь Am-Ce-Cs-Co-Y.xml'),
]


def main():
    ok = missing = 0
    for folder, src, name in FILES:
        target_dir = os.path.join(DEST, folder)
        os.makedirs(target_dir, exist_ok=True)
        if not os.path.isfile(src):
            print('НЕТ ИСХОДНИКА: %s' % src)
            missing += 1
            continue
        target = os.path.join(target_dir, name)
        shutil.copyfile(src, target)
        ok += 1
        print('%-18s %-40s %8d байт' % (folder, name[:40], os.path.getsize(target)))
    print('\nперенесено %d, не найдено %d -> %s' % (ok, missing, DEST))


main()
