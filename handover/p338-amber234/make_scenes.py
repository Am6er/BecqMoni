import sys, re
sys.stdout.reconfigure(encoding='utf-8')
TPL = r'C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/tools/CORPUS/corpus/geometries/G1S_mar1l_coal_046_p24.in'
OUT = 'D:/BqMoni_Claude/p338/scenes/'
src = open(TPL, 'rb').read().decode('cp1251').split('\r\n')
AIR = ('Air, dry', 0.001205, [(6, 0.000124), (7, 0.755268), (8, 0.231781), (18, 0.012827)])
NAI = ('Sodium iodide', 3.667, [(11, 0.153373), (53, 0.846627)])
MGO = ('MgO powder (G4 GS2020)', 1.8, [(12, 0.603), (8, 0.397)])
AL = ('Aluminum', 2.7, [(13, 1.0)])
PP = ('Polypropylene (G4 GS2020)', 0.905, [(1, 0.143711), (6, 0.856289)])
OISN = ('OISN-10 Epoxy_crumb (G4 GS2020)', 0.9061, [(1, 0.044), (6, 0.31), (7, 0.036), (8, 0.12), (26, 0.49)])
BERRY = ('Dried blueberry Epoxy_crumb (G4 GS2020)', 0.482, [(1, 0.071), (6, 0.386), (7, 0.005), (8, 0.532), (19, 0.006)])
def setkey(L, key, val):
    pat = re.compile(r'^' + re.escape(key) + r'\s*=')
    idx = [i for i, l in enumerate(L) if pat.match(l)]
    assert len(idx) == 1, (key, idx)
    L[idx[0]] = f'{key} = {val}'
def setmat(L, nkey, mat):
    """nkey = e.g. 'DS_nCrystalElements'; block runs to the line '<M_prefix>.MatRelWeight[0] = 1'."""
    i0 = [i for i, l in enumerate(L) if l.startswith(nkey + ' ')]
    assert len(i0) == 1, nkey
    i0 = i0[0]; i1 = i0
    while not re.match(r'^M_[A-Za-z_]+\.MatRelWeight\[0\]', L[i1]): i1 += 1
    blk = L[i0:i1 + 1]
    ro = next(l.split(' =')[0] for l in blk if re.match(r'^[A-Z]+_Ro', l))
    zk = next(l.split('[')[0] for l in blk if re.match(r'^[A-Z]+_Z[A-Za-z]+\[0\]', l))
    fk = next(l.split('[')[0] for l in blk if re.match(r'^[A-Z]+_Fractions[A-Za-z]+\[0\]', l))
    ft = next(l for l in blk if re.match(r'^[A-Z]+_FractionType', l))
    mp = next(l.split('.MName')[0] for l in blk if '.MName' in l)
    name, rho, els = mat
    new = [f'{nkey} = {len(els)}', f'{ro} = {rho:g}']
    for j, (z, f) in enumerate(els): new += [f'{zk}[{j}] = {z}', f'{fk}[{j}] = {f:g}']
    new += [ft, f'{mp}.MName = {name}', f'{mp}.Nmaterials = 1', f'{mp}.Name[0] = {name}', f'{mp}.MatRelWeight[0] = 1']
    L[i0:i1 + 1] = new
def scene(fname, vessel, sample, title):
    L = list(src)
    det = dict(DS_CrystalDiameter='5.08 cm', DS_CrystalHeight='5.08 cm', DS_CrystalFrontReflectorThickness='0.31 cm',
               DS_CrystalSideReflectorThickness='0.31 cm', DS_CrystalFrontGapThickness='0.21 cm', DS_CrystalSideGapThickness='0.024 cm',
               DS_CrystalFrontCladdingThickness='0.275 cm', DS_CrystalSideCladdingThickness='0.275 cm', DS_DetectorMountingThickness='0.3 cm')
    for k, v in {**det, **vessel}.items(): setkey(L, k, v)
    setmat(L, 'DS_nCrystalElements', NAI); setmat(L, 'DS_nCrystalReflectorElements', MGO); setmat(L, 'DS_nCrystalCladdingElements', AL)
    setmat(L, 'DS_nCrystalGapElements', AIR); setmat(L, 'SM_nWallElements', PP); setmat(L, 'SM_nSourceElements', sample); setmat(L, 'SM_nEmptySpaceElements', AIR)
    setkey(L, 'DS_Fwhm662', '7.29 %')
    L.insert(0, '// ' + title)
    open(OUT + fname, 'wb').write('\r\n'.join(L).encode('cp1251'))
    print('записано', fname)
TH = dict(SM_BeakerToDetectorFrontDistance='0 cm', SM_BeakerDiameter='14.5 cm', SM_BeakerHeight='10.9 cm', SM_BeakerHoleDiameter='8.2 cm',
          SM_BeakerHoleHeight='5.55 cm', SM_BeakerSideThickness='0.2 cm', SM_BeakerEndWallThickness='0.2 cm', SM_BeakerHoleSideThickness='0.2 cm',
          SM_BeakerHoleEndWallThickness='0.2 cm', SM_SourceHeight='9.5 cm')
BE = dict(SM_BeakerToDetectorFrontDistance='0 cm', SM_BeakerDiameter='14.4 cm', SM_BeakerHeight='10.9 cm', SM_BeakerHoleDiameter='7.8 cm',
          SM_BeakerHoleHeight='5.7 cm', SM_BeakerSideThickness='0.293 cm', SM_BeakerEndWallThickness='0.3 cm', SM_BeakerHoleSideThickness='0.295 cm',
          SM_BeakerHoleEndWallThickness='0.3 cm', SM_SourceHeight='8.778 cm')
scene('GS2020_mar1l_oisn10.in', TH, OISN, 'П338 AMBER234: Atom GS2020 PRO MAX (NaI D50.8xH50.8, MgO 3.1, Al 1.0+1.75) + Маринелли 1 л, ОИСН-10 по GDML geometry_marinelli1l_oisn10 (VibeEngineering)')
scene('GS2020_mar1l_berry.in', BE, BERRY, 'П338 AMBER234: Atom GS2020 PRO MAX + Маринелли 1 л (второй сосуд), сушёная черника 482 г по GDML GS2020_marinelli_berry_dry_1000ml (VibeEngineering)')
