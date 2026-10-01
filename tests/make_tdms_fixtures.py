import pathlib,numpy as np,struct
from nptdms import TdmsWriter,RootObject,GroupObject,ChannelObject
root=pathlib.Path(__file__).resolve().parent/'artifacts/tdms-fixtures';root.mkdir(exist_ok=True)
x=np.arange(2048,dtype=np.int16)-1024;y=-x
expected=np.column_stack((x,y)).astype('<i2').tobytes()
def write(name,a=x,b=y,props=None,segments=1,names=('I','Q'),order=False):
 p=root/(name+'.tdms'); ch=[ChannelObject('IQ',names[0],a),ChannelObject('IQ',names[1],b)]
 if order:ch=ch[::-1]
 with TdmsWriter(p) as w:
  for n in range(segments):w.write_segment([RootObject(props or {'rate_hz':120e6}),GroupObject('IQ'),*ch])
 if not name.startswith('reject-'):p.with_suffix('.expected').write_bytes(expected*segments)
write('int16');write('reverse-channel-order',order=True);write('segmented',segments=3)
write('float32',x.astype(np.float32)/32767,y.astype(np.float32)/32767)
write('float64',x.astype(np.float64)/32767,y.astype(np.float64)/32767)
write('wf-increment',props={'wf_increment':1/120e6})
write('reject-rate',props={'rate_hz':122.88e6});write('reject-no-rate',props={'model':'unknown'})
write('reject-names',names=('A','B'));write('reject-count',b=y[:-1])
write('reject-nan',a=np.full(2048,np.nan));write('reject-out-of-range',a=np.full(2048,1.1))
p=root/'reject-truncated.tdms';p.write_bytes((root/'int16.tdms').read_bytes()[:-7])
# Convert a legal contiguous fixture into interleaved layout, independent of the reader.
p=root/'interleaved.tdms';raw=bytearray((root/'int16.tdms').read_bytes());toc=struct.unpack_from('<I',raw,4)[0];struct.pack_into('<I',raw,4,toc|32);offset=28+struct.unpack_from('<Q',raw,20)[0];raw[offset:]=expected;p.write_bytes(raw);p.with_suffix('.expected').write_bytes(expected)
print(root)
