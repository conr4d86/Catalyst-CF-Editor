#pragma once
#define N_LAYERS 23
#define N_WEIGHTS 1871
#define HISTORY_FLOATS 19110
#define HEAD_RING_OFFSET 19062
#define HEAD_WEIGHT_OFFSET 1821
static const unsigned kernels[23] = {6,6,6,6,6,6,6,6,6,6,6,6,6,6,15,15,6,6,6,6,6,6,6};
static const unsigned dilations[23] = {1,3,7,17,41,101,239,1,3,7,17,41,101,239,1,13,1,3,7,17,41,101,239};
static const unsigned ring_offset[23] = {0,18,66,174,432,1050,2568,6156,6174,6222,6330,6588,7206,8724,12312,12357,12906,12924,12972,13080,13338,13956,15474};
static const unsigned ring_length[23] = {6,16,36,86,206,506,1196,6,16,36,86,206,506,1196,15,183,6,16,36,86,206,506,1196};
static const unsigned weight_offset[23] = {3,75,147,219,291,363,435,507,579,651,723,795,867,939,1011,1164,1317,1389,1461,1533,1605,1677,1749};
