#include "TxConvert.hpp"
#include <vector>
#include <random>
#include <chrono>
#include <iostream>
#include <limits>
#include <cmath>
__declspec(noinline) uint64_t scalar(const float* in,int16_t* out,size_t n,float gain) {
 uint64_t clips=0;
 for(size_t i=0;i<n;i++) {
  float a=in[2*i]*gain,b=in[2*i+1]*gain;if(a>1||a< -1||b>1||b< -1)++clips;
  a=std::max(-1.f,std::min(1.f,a))*32767.f;b=std::max(-1.f,std::min(1.f,b))*32767.f;
  out[2*i]=static_cast<int16_t>(a>=0?a+.5f:a-.5f);out[2*i+1]=static_cast<int16_t>(b>=0?b+.5f:b-.5f);
 }
 return clips;
}
int main() {
 std::mt19937 generator(5644);std::uniform_real_distribution<float> random(-1.5f,1.5f);
 const size_t n=262144;std::vector<float> x(2*n+17);std::vector<int16_t> a(2*n+17),b(2*n+17);
 for(auto& f:x)f=random(generator);
 x[0]=std::numeric_limits<float>::quiet_NaN();x[1]=std::numeric_limits<float>::infinity();x[2]=-x[1];x[3]=-0.f;
 for(float gain:{0.f,.1f,1.f,2.f})for(size_t offset:{size_t(0),size_t(1)})for(size_t count:{size_t(1),size_t(3),size_t(4),n+3}){
  auto ca=scalar(x.data()+offset,a.data(),count,gain),cb=vst::convertTxCf32(x.data()+offset,b.data(),count,gain);
  if(ca!=cb||!std::equal(a.begin(),a.begin()+count*2,b.begin())){std::cerr<<"Mismatch gain="<<gain<<" count="<<count<<"\n";return 1;}
 }
 for(size_t i=0;i<2*n;i++)x[i]=.5f*std::sin(float(i)*3.14159265358979323846f/120.f);
 auto bench=[&](auto fn){auto t=std::chrono::steady_clock::now();uint64_t sum=0;for(int j=0;j<300;j++)sum+=fn(x.data(),a.data(),n,1.f);std::cout<<"clips="<<sum<<" ";return double(n)*300/std::chrono::duration<double>(std::chrono::steady_clock::now()-t).count()/1e6;};
 std::cout<<"Exact scalar equivalence: PASS\nscalar_msps="<<bench(scalar)<<"\nsse2_msps="<<bench(vst::convertTxCf32)<<"\n";
}
