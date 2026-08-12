#include "fastcpp.h"
#include <cstdio>
#include <cmath>
#include <vector>
#include <algorithm>
int main(){
    const int W=256,H=256,tw=41,th=31,px=80,py=70;
    std::vector<unsigned char> scene((size_t)W*H,128);
    std::vector<unsigned char> templ((size_t)tw*th,128);
    int cx=tw/2,cy=th/2;
    for(int j=0;j<th;++j)for(int i=0;i<tw;++i)
        if(std::abs(i-cx)<=4||std::abs(j-cy)<=4) templ[(size_t)j*tw+i]=230;
    for(int j=0;j<th;++j)for(int i=0;i<tw;++i)
        scene[(size_t)(py+j)*W+(px+i)]=templ[(size_t)j*tw+i];
    fastcpp::FastMatcher fm;
    fm.setSource(scene.data(),W,H,W);
    fm.setTemplate(templ.data(),tw,th,tw);
    auto r=fm.match(0,0,1,-2.0,0.1,200);
    std::sort(r.begin(),r.end(),[](auto&a,auto&b){return a.score>b.score;});
    printf("fastcpp top3:\n");
    for(int i=0;i<std::min(3,(int)r.size());++i)
        printf("  #%d score=%.4f cx=%.1f cy=%.1f (topLeft x=%.1f y=%.1f)\n",i,r[i].score,r[i].centerX,r[i].centerY,r[i].centerX-tw/2.0,r[i].centerY-th/2.0);
    // brute with guard
    auto ncc=[&](int x,int y){
        int N=tw*th; double sI=0,sI2=0;
        for(int j=0;j<th;++j)for(int i=0;i<tw;++i){double v=scene[(size_t)(y+j)*W+(x+i)];sI+=v;sI2+=v*v;}
        double mT=0;for(unsigned char v:templ)mT+=v;mT/=N;
        double vT=0;for(unsigned char v:templ)vT+=(v-mT)*(v-mT);
        double cr=0;for(int j=0;j<th;++j)for(int i=0;i<tw;++i)cr+=(double)scene[(size_t)(y+j)*W+(x+i)]*templ[(size_t)j*tw+i];
        double vI=sI2-sI*sI/N; if(vI<=1e-6||vT<=1e-6) return -9.0; return cr/(sqrt(vI)*sqrt(vT));
    };
    double best=-9;int bx=0,by=0;
    for(int y=0;y+th<=H;++y)for(int x=0;x+tw<=W;++x){double v=ncc(x,y);if(v>best){best=v;bx=x;by=y;}}
    printf("brute best ncc=%.4f at topLeft x=%d y=%d\n",best,bx,by);
    return 0;
}
