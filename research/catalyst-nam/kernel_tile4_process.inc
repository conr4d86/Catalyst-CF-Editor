/* Four output samples share each 3x3 weight matrix in registers. */
void nam_process(NamState *__restrict s, const float *__restrict model,
                 const float *input, float *output, unsigned count) {
    while(count) {
        unsigned block=count>32?32:count;
        float x[32][3],hs[32][3],a[32][3];
        for(unsigned n=0;n<block;++n)for(unsigned j=0;j<3;++j) {
            x[n][j]=model[j]*input[n];hs[n][j]=0;
        }
        for(unsigned l=0;l<N_LAYERS;++l) {
            const float *w=model+weight_offset[l];
            unsigned len=ring_length[l],k=kernels[l],d=dilations[l],start=s->write[l];
            float *history=s->history+ring_offset[l];
            for(unsigned n=0;n<block;n+=4) {
                #define INIT(Q) \
                    const float in##Q=n+Q<block?input[n+Q]:0; \
                    float v##Q##0=w[0]+w[3]*in##Q; \
                    float v##Q##1=w[1]+w[4]*in##Q; \
                    float v##Q##2=w[2]+w[5]*in##Q;
                INIT(0) INIT(1) INIT(2) INIT(3)
                #undef INIT
                for(unsigned t=0;t<k;++t) {
                    const float *cw=w+6+t*9;
                    const float w00=cw[0],w01=cw[1],w02=cw[2];
                    const float w10=cw[3],w11=cw[4],w12=cw[5];
                    const float w20=cw[6],w21=cw[7],w22=cw[8];
                    unsigned delay=(k-1-t)*d;
                    #define STEP(Q) do { \
                        const float *p; \
                        if(n+Q>=block)p=x[0]; \
                        else if(n+Q>=delay)p=x[n+Q-delay]; \
                        else {unsigned pos=start+len+n+Q-delay;if(pos>=len)pos-=len;p=history+pos*3;} \
                        const float r0=p[0],r1=p[1],r2=p[2]; \
                        v##Q##0+=w00*r0;v##Q##1+=w01*r0;v##Q##2+=w02*r0; \
                        v##Q##0+=w10*r1;v##Q##1+=w11*r1;v##Q##2+=w12*r1; \
                        v##Q##0+=w20*r2;v##Q##1+=w21*r2;v##Q##2+=w22*r2; \
                    } while(0)
                    STEP(0);STEP(1);STEP(2);STEP(3);
                    #undef STEP
                }
                #define STORE(Q) do {a[n+Q][0]=v##Q##0;a[n+Q][1]=v##Q##1;a[n+Q][2]=v##Q##2;} while(0)
                STORE(0);STORE(1);STORE(2);STORE(3);
                #undef STORE
            }
            const float *rw=w+6+k*9;
            unsigned pos=start;
            for(unsigned n=0;n<block;++n) {
                for(unsigned j=0;j<3;++j)history[pos*3+j]=x[n][j];
                if(++pos==len)pos=0;
                float a0=a[n][0],a1=a[n][1],a2=a[n][2];
                if(a0<0)a0*=.01f;if(a1<0)a1*=.01f;if(a2<0)a2*=.01f;
                hs[n][0]+=a0;hs[n][1]+=a1;hs[n][2]+=a2;
                float v0=rw[0],v1=rw[1],v2=rw[2];
                v0+=rw[3]*a0;v1+=rw[4]*a0;v2+=rw[5]*a0;
                v0+=rw[6]*a1;v1+=rw[7]*a1;v2+=rw[8]*a1;
                v0+=rw[9]*a2;v1+=rw[10]*a2;v2+=rw[11]*a2;
                x[n][0]+=v0;x[n][1]+=v1;x[n][2]+=v2;
            }
            s->write[l]=pos;
        }
        float *hr=s->history+HEAD_RING_OFFSET;unsigned pos=s->write[N_LAYERS];
        const float *head=model+HEAD_WEIGHT_OFFSET;
        for(unsigned n=0;n<block;++n) {
            for(unsigned j=0;j<3;++j)hr[pos*3+j]=hs[n][j];
            if(++pos==16)pos=0;
            unsigned tap=pos;const float *w=head;float y=*w++;
            for(unsigned t=0;t<16;++t) {
                for(unsigned j=0;j<3;++j)y+=*w++*hr[tap*3+j];
                if(++tap==16)tap=0;
            }
            output[n]=y*model[N_WEIGHTS-1];
        }
        s->write[N_LAYERS]=pos;
        input+=block;output+=block;count-=block;
    }
}
unsigned nam_state_bytes(void) {return sizeof(NamState);}
