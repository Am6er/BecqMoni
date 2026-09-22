import struct, sys, hashlib
sys.path.insert(0, r'D:\BqMoni_Claude\p140')
import rmx_qk
for name in ('RC103_point0','AS80_point0'):
    p = r'D:\BqMoni_Claude\p140\store_ctrl\%s.rmx' % name
    m = rmx_qk.read(p)
    b = open(p,'rb').read()
    # число каналов лежит сразу за сеткой узлов
    pos = 8
    s, pos = rmx_qk.read_string(b, pos)
    pos += 8+4+8+8+44
    nodes = struct.unpack_from('<i', b, pos)[0]; pos += 4 + 8*nodes
    ch = struct.unpack_from('<i', b, pos)[0]
    print('%-14s формат %d  клеймо %s  узлов %d  каналов %d  Q_k узлов %d  тело sha %s'
          % (name, m['format'], m['stamp'][:24], m['nodes'], ch, len(m['qk'] or []), m['body_sha'][:16]))