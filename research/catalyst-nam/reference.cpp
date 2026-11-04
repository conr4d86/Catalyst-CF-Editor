// Host-only comparison with pinned official NAM, not an embedded dependency.
#include <fstream>
#include <vector>
#include <iterator>
#include "NAM/dsp.h"
#include "NAM/wavenet/a2_fast.h"
int main(int argc,char**argv) {
    if (argc != 4) return 2;
    std::ifstream jf(argv[1]); nlohmann::json j; jf >> j;
    if (j["architecture"] == "SlimmableContainer") {
        nlohmann::json selected;
        for(auto &s:j["config"]["submodels"])
            if(s["model"]["config"]["layers"][0]["channels"] == 3) selected=s["model"];
        j=selected;
    }
    int ch=0;
    if(!nam::wavenet::a2_fast::is_a2_shape(j["config"], &ch) || ch!=3) return 3;
    auto config=nam::wavenet::a2_fast::create_a2_fast_config(j["config"],48000);
    auto model=config->create(j["weights"].get<std::vector<float>>(),48000);
    model->Reset(48000,32); // Includes official prewarm.
    std::ifstream f(argv[2],std::ios::binary);
    std::vector<char> bytes((std::istreambuf_iterator<char>(f)),{});
    if(bytes.size()%128) return 4;
    std::vector<float> in(bytes.size()/4), out(in.size());
    std::memcpy(in.data(),bytes.data(),bytes.size());
    for(size_t n=0;n<in.size();n+=32) {
        float *a=in.data()+n,*b=out.data()+n; model->process(&a,&b,32);
    }
    std::ofstream o(argv[3],std::ios::binary);
    o.write(reinterpret_cast<const char*>(out.data()),out.size()*4);
    return 0;
}
