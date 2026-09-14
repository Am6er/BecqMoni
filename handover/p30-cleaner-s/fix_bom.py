import io
p='tools/effmaker/probes/CorpusFsaProbe.cs'
raw=io.open(p,'rb').read()
bad=b"TrimStart('\xef\xbb\xbf')"
good=b"TrimStart('" + b"\\" + b"uFEFF')"
assert raw.count(bad)==1, raw.count(bad)
raw=raw.replace(bad,good)
io.open(p,'wb').write(raw)
print(raw.count(b'\r\n'), raw.count(b'\n'))
