#include "SoapyVst.hpp"
#include <SoapySDR/Registry.hpp>
#include <niRFSA.h>
#include <cstdio>

using namespace vst;

static SoapySDR::KwargsList findVst(const SoapySDR::Kwargs &args)
{
  SoapySDR::KwargsList results;
  // Prefer explicit resource; otherwise advertise RIO0 (MAX alias for this chassis).
  SoapySDR::Kwargs info;
  info["driver"] = "vst";
  info["label"] = "NI PXIe-5644R (RIO0)";
  info["resource"] = "RIO0";
  if (args.count("resource"))
    info["resource"] = args.at("resource");
  if (args.count("backend"))
    info["backend"] = args.at("backend");
  else
    info["backend"] = "dma";
  results.push_back(info);
  return results;
}

static SoapySDR::Device *makeVst(const SoapySDR::Kwargs &args)
{
  return new Device(args);
}

static SoapySDR::Registry registerVst("vst", &findVst, &makeVst, SOAPY_SDR_ABI_VERSION);
