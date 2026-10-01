#pragma once
#include <emmintrin.h>
#include <algorithm>
#include <cstddef>
#include <cstdint>

namespace vst {
// SSE2 is baseline on x64. Clip count remains one per complex IQ sample.
inline uint64_t convertTxCf32(const float* input,int16_t* output,size_t samples,float gain) {
  const __m128 g=_mm_set1_ps(gain),one=_mm_set1_ps(1.f),minusOne=_mm_set1_ps(-1.f);
  const __m128 scale=_mm_set1_ps(32767.f),half=_mm_set1_ps(.5f),sign=_mm_set1_ps(-0.f);
  uint64_t clips=0;size_t i=0,components=samples*2;
  auto convert=[&](__m128 x) {
    x=_mm_mul_ps(x,g);
    const int mask=_mm_movemask_ps(_mm_or_ps(_mm_cmpgt_ps(x,one),_mm_cmplt_ps(x,minusOne)));
    clips+=((mask&3)!=0)+((mask&12)!=0);
    x=_mm_max_ps(minusOne,_mm_min_ps(x,one));
    const __m128 offset=_mm_or_ps(half,_mm_and_ps(x,sign));
    return _mm_cvttps_epi32(_mm_add_ps(_mm_mul_ps(x,scale),offset));
  };
  for(;i+8<=components;i+=8) {
    const __m128i a=convert(_mm_loadu_ps(input+i)),b=convert(_mm_loadu_ps(input+i+4));
    _mm_storeu_si128(reinterpret_cast<__m128i*>(output+i),_mm_packs_epi32(a,b));
  }
  for(;i<components;i+=2) {
    float a=input[i]*gain,b=input[i+1]*gain;
    if(a>1.f||a< -1.f||b>1.f||b< -1.f)++clips;
    a=std::max(-1.f,std::min(1.f,a))*32767.f;b=std::max(-1.f,std::min(1.f,b))*32767.f;
    output[i]=static_cast<int16_t>(a>=0.f?a+.5f:a-.5f);
    output[i+1]=static_cast<int16_t>(b>=0.f?b+.5f:b-.5f);
  }
  return clips;
}
}
