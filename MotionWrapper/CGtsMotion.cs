﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Threading;
using MotionWrapper.Interface;
using MotionWrapper.BaseClass;
using MotionWrapper.PubicData;
using gts;

namespace MotionWrapper
{
    /// <summary>
    /// 固高卡的封装  都在这里
    /// 数据区域都在这个类里面
    /// 每次项目  这个类需要重写的
    /// </summary>
    public class CGtsMotion : CMotionData,IMotionPart1, IMotionPart5, IIoPart1,IInitModel,IFreshModel
    {
        public const int usedInput = 16, usedOutput = 16, usedAxis = 5;//必须外部指定
        public bool initOk = false;

        private System.Threading.Thread runThread = null;       //用来刷新IO的
        //私有变量
        private short cardNum = 0;
        //局部变量
        public CGtsMotion()
        {
            for (int i = 0; i < maxAxisNum; i++)
            {
                axisRefs[i] = new AxisRef();
            }
        }
        //局部变量
        bool IIoPart1.getDi(CIoMap input)
        {
            int pos = input.adress;// CIoPrms.i[].dataindex;
            return mdis[pos];
        }

        bool IIoPart1.getDo(CIoMap output)
        {
            int pos = output.adress;// CIoPrms.o[].dataindex;
            return mdos[pos];
        }

        int IMotionPart1.MC_Home(ref AxisRef axis)
        {
            mc.THomePrm prm = new mc.THomePrm();
            axis.isHoming = false;
            short rtn = mc.GT_GetHomePrm(axis.prm.cardNum, axis.prm.axisNum, out prm);
            rtn = mc.GT_ZeroPos(axis.prm.cardNum, axis.prm.axisNum, 1);
            rtn = mc.GT_ClrSts(axis.prm.cardNum, axis.prm.axisNum, 1);
            prm.mode = axis.prm.homeType;
            prm.acc = axis.prm.getAccPlsPerMs2();
            prm.dec = prm.acc;
            if (axis.prm.homeSearch >= 0)
            {
                prm.moveDir = 1;
            }
            else
            {
                prm.moveDir = -1;
            }
            prm.indexDir = 1;
            prm.edge = 0;
            prm.homeOffset = (int)axis.prm.mm2pls(axis.prm.homeoffset);
            prm.velHigh = axis.prm.mmpers2plsperms(axis.prm.homeVel1);
            prm.velLow = axis.prm.mmpers2plsperms(axis.prm.homeVel2);
            prm.searchHomeDistance = Math.Abs((int)axis.prm.mm2pls(axis.prm.homeSearch));
            prm.searchIndexDistance = prm.searchHomeDistance;
            prm.escapeStep = (int)axis.prm.mm2pls(axis.prm.homeLeave);
            rtn += mc.GT_GoHome(axis.prm.cardNum, axis.prm.axisNum, ref prm);
            if (rtn == 0) axis.isHoming = true;
            else axis.isHoming = false;
            return rtn;
        }

        int IMotionPart1.MC_MoveAbs(AxisRef axis, double tpos, double beilv)
        {
            short rtn = 0;
            int pmode;
            int psts;
            uint pclock;
            rtn += mc.GT_GetPrfMode(axis.prm.cardNum, axis.prm.axisNum, out pmode, 1, out pclock);
            rtn += mc.GT_GetSts(axis.prm.cardNum, axis.prm.axisNum, out psts, 1, out pclock);
            if ((psts & 0x400) == 0)
            {
                if (pmode != 0)
                {
                    rtn += mc.GT_PrfTrap(axis.prm.cardNum, axis.prm.axisNum);
                }
                mc.TTrapPrm trapprm = new mc.TTrapPrm();
                trapprm.acc = axis.prm.getAccPlsPerMs2();
                trapprm.dec = axis.prm.getAccPlsPerMs2();
                trapprm.smoothTime = 50;
                trapprm.velStart = 0;
                rtn += mc.GT_SetTrapPrm(axis.prm.cardNum, axis.prm.axisNum, ref trapprm);
                rtn += mc.GT_SetVel(axis.prm.cardNum, axis.prm.axisNum, axis.prm.getVelPlsPerMs() * beilv);
                rtn += mc.GT_SetPos(axis.prm.cardNum, axis.prm.axisNum, (int)(axis.prm.mm2pls(tpos)));
                //int sts = 0;
                //uint clock = 0;
                //rtn = mc.GT_GetSts(cardnum, relAxis, out sts, 1, out clock);
                rtn += mc.GT_Update(axis.prm.cardNum, 1 << (axis.prm.axisNum - 1));
            }
            else if (pmode == 0)
            {
                rtn += mc.GT_SetVel(axis.prm.cardNum, axis.prm.axisNum, axis.prm.getVelPlsPerMs() * beilv);
                rtn += mc.GT_SetPos(axis.prm.cardNum, axis.prm.axisNum, (int)(axis.prm.mm2pls(tpos)));
                rtn += mc.GT_Update(axis.prm.cardNum, 1 << (axis.prm.axisNum - 1));
            }
            return rtn;
        }

        int IMotionPart1.MC_MoveAdd(AxisRef axis, double dist, double beilv)
        {
            short rtn = 0;
            int pmode;
            int psts;
            uint pclock;
            double prfpos;
            rtn += mc.GT_GetPrfMode(axis.prm.cardNum, axis.prm.axisNum, out pmode, 1, out pclock);
            rtn += mc.GT_GetSts(axis.prm.cardNum, axis.prm.axisNum, out psts, 1, out pclock);
            if ((psts & 0x400) == 0)
            {
                if (pmode != 0)
                {
                    rtn += mc.GT_PrfTrap(axis.prm.cardNum, axis.prm.axisNum);
                }
                mc.TTrapPrm trapprm = new mc.TTrapPrm();
                trapprm.acc = axis.prm.getAccPlsPerMs2();
                trapprm.dec = axis.prm.getAccPlsPerMs2();
                trapprm.smoothTime = 50;
                trapprm.velStart = 0;
                rtn += mc.GT_SetTrapPrm(axis.prm.cardNum, axis.prm.axisNum, ref trapprm);
                rtn += mc.GT_SetVel(axis.prm.cardNum, axis.prm.axisNum, axis.prm.getVelPlsPerMs() * beilv);
                rtn += mc.GT_GetPrfPos(axis.prm.cardNum, axis.prm.axisNum, out prfpos, 1, out pclock);
                rtn += mc.GT_SetPos(axis.prm.cardNum, axis.prm.axisNum, (int)(axis.prm.mm2pls(dist) + prfpos));
                rtn += mc.GT_Update(axis.prm.cardNum, 1 << (axis.prm.axisNum - 1));
            }
            return rtn;
        }

        int IMotionPart1.MC_MoveJog(AxisRef axis, double beilv)
        {
            try
            {
                short rtn = 0;
                int pmode;
                int psts;
                uint pclock;
                rtn += mc.GT_GetPrfMode(axis.prm.cardNum, axis.prm.axisNum, out pmode, 1, out pclock);
                rtn += mc.GT_GetSts(axis.prm.cardNum, axis.prm.axisNum, out psts, 1, out pclock);
                if ((psts & 0x400) == 0)
                {
                    if (pmode != 1)
                    {
                        rtn = mc.GT_PrfJog(axis.prm.cardNum, axis.prm.axisNum);
                    }
                    mc.TJogPrm jogprm = new mc.TJogPrm();
                    jogprm.acc = axis.prm.getAccPlsPerMs2();
                    jogprm.dec = axis.prm.getAccPlsPerMs2();
                    jogprm.smooth = 0.5;
                    rtn = mc.GT_SetJogPrm(axis.prm.cardNum, axis.prm.axisNum, ref jogprm);
                    rtn = mc.GT_SetVel(axis.prm.cardNum, axis.prm.axisNum, axis.prm.getVelPlsPerMs() * beilv);
                    rtn = mc.GT_ClrSts(axis.prm.cardNum, axis.prm.axisNum, 1);
                    rtn = mc.GT_Update(axis.prm.cardNum, 1 << (axis.prm.axisNum - 1));
                }
                else if (pmode == 1)
                {
                    rtn = mc.GT_SetVel(axis.prm.cardNum, axis.prm.axisNum, axis.prm.getVelPlsPerMs() * beilv);
                    rtn = mc.GT_ClrSts(axis.prm.cardNum, axis.prm.axisNum, 1);
                    rtn = mc.GT_Update(axis.prm.cardNum, 1 << (axis.prm.axisNum - 1));
                }
                return rtn;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        int IMotionPart1.MC_Power(AxisRef axis)
        {
            return mc.GT_AxisOn(axis.prm.cardNum, axis.prm.axisNum);
        }

        int IMotionPart1.MC_Reset(AxisRef axis)
        {
            mc.GT_ClrSts(axis.prm.cardNum, axis.prm.axisNum, 1);
            if (axis.alarm)
            {
                mc.GT_SetDoBitReverse(axis.prm.cardNum, mc.MC_CLEAR, axis.prm.axisNum, 0, 500);
            }
            return 0;
        }

        void IIoPart1.setDO(CIoMap output,bool value)
        {
            if (value)
            {
                mc.GT_SetDoBit((short)CIoPrms.o[output.adress].modelNum, mc.MC_GPO, (short)(CIoPrms.o[output.adress].index+1),0);
            }
            else
            {
                mc.GT_SetDoBit((short)CIoPrms.o[output.adress].modelNum, mc.MC_GPO, (short)(CIoPrms.o[output.adress].index+1), 1);
            }
        }
        private void subStatusData()
        {
            short rtn = 0;
            int inValue = 0,outValue = 0;
            uint clock = 0;
            double[] prfpos = new double[8];
            double[] encpos = new double[8];
            double[] encvel = new double[8];
            //辅助编码器
            double[] fzencpos = new double[2];
            double[] fzencvel = new double[2];
            int[] sts = new int[8];
            rtn = mc.GT_GetDi(0, mc.MC_GPI, out inValue);
            rtn = mc.GT_GetDo(0, mc.MC_GPO, out outValue);
            rtn = mc.GT_GetPrfPos(0, 1, out prfpos[0], 4, out clock);
            rtn = mc.GT_GetEncPos(0, 1, out encpos[0], 4, out clock);
            rtn = mc.GT_GetEncVel(0, 1, out encvel[0], 4, out clock);
            rtn = mc.GT_GetSts(0, 1, out sts[0], 4, out clock);
            //辅助编码器
            rtn = mc.GT_GetEncPos(0, 9, out fzencpos[0], 2, out clock);
            rtn = mc.GT_GetEncVel(0, 9, out fzencvel[0], 2, out clock);
            //解析
            for (int i = 0; i < 16; i++)
            {
                mdis[i] = ((1 << i) & inValue) == 0;
                mdos[i] = ((1 << i) & outValue) == 0;
                if (i<4)//轴状态分解
                {
                    axisRefs[i].alarm = (sts[i] & 0x2) != 0;
                    axisRefs[i].servoOn = (sts[i] & 0x200) != 0;
                    axisRefs[i].limitN = (sts[i] & 0x40) != 0;
                    axisRefs[i].limitP = (sts[i] & 0x20) != 0;
                    axisRefs[i].moving = (sts[i] & 0x400) != 0;
                    
                    axisRefs[i].cmdPos = (float)AxisParameters.prms[i].pls2mm((long)prfpos[i]);
                    axisRefs[i].relPos = (float)AxisParameters.prms[i].pls2mm((long)encpos[i]);
                    axisRefs[i].relVel = (float)AxisParameters.prms[i].plsperms2mmpers(encvel[i]);
                }
                if (i ==4)//辅助
                {
                    axisRefs[i].relPos = (float)AxisParameters.prms[i].pls2mm((long)fzencpos[i-4]);
                    axisRefs[i].relVel = (float)AxisParameters.prms[i].plsperms2mmpers(fzencvel[i-4]);
                    axisRefs[i].alarm = false;
                    axisRefs[i].servoOn = false;
                    axisRefs[i].limitN = false;
                    axisRefs[i].limitN = false;
                    axisRefs[i].moving = axisRefs[i].relVel != 0;
                }
            }
        }

        public int MC_AxisRef(ref AxisRef axisref)
        {
            short rtn = 0;
            uint clock = 0;
            double[] prfpos = new double[1];
            double[] encpos = new double[1];
            double[] encvel = new double[1];
            int[] sts = new int[1];
            rtn += mc.GT_GetPrfPos(0, 1, out prfpos[0], 1, out clock);
            rtn += mc.GT_GetEncPos(0, 1, out encpos[0], 1, out clock);
            rtn += mc.GT_GetEncVel(0, 1, out encvel[0], 1, out clock);
            rtn += mc.GT_GetSts(0, 1, out sts[0], 1, out clock);
            //解析
            axisref.alarm = (sts[0] & 0x2) != 0;
            axisref.servoOn = (sts[0] & 0x200) != 0;
            axisref.limitN = (sts[0] & 0x40) != 0;
            axisref.limitP = (sts[0] & 0x20) != 0;
            axisref.moving = (sts[0] & 0x400) != 0;
            
            axisref.cmdPos = (float)axisref.prm.pls2mm((long)prfpos[0]);
            axisref.relPos = (float)axisref.prm.pls2mm((long)encpos[0]);
            axisref.relVel = (float)axisref.prm.plsperms2mmpers(encvel[0]);

            return rtn;
        }

        int IMotionPart1.MC_EStop(AxisRef axis)
        {
            axis.isHoming = false;
            mc.GT_Stop(axis.prm.cardNum, 1 << (axis.prm.axisNum - 1), 1 << (axis.prm.axisNum - 1));
            return 0;
        }

        int IMotionPart1.MC_PowerOff(AxisRef axis)
        {
            return mc.GT_AxisOff(axis.prm.cardNum, axis.prm.axisNum);
        }

        int IMotionPart5.MC_CrdData(string cmd, int crdNum)
        {
            throw new NotImplementedException();
        }
        int IMotionPart5.MC_CrdData(List<string> gcode, int crdNum)
        {
            throw new NotImplementedException();
        }

        int IMotionPart5.MC_Cam(string camlist)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// 提前进入cam模式  防止浪费时间
        /// </summary>
        /// <param name="master"></param>
        /// <param name="slaver"></param>
        /// <returns></returns>
        int IMotionPart5.preMC_CamModel(AxisRef master, AxisRef slaver)
        {
            int model = 0;
            uint clock = 0;
            //判断是否是跟随模式
            short rtn = mc.GT_GetPrfMode(slaver.prm.cardNum, slaver.prm.axisNum, out model, 1, out clock);
            if (model != 4)
            {
                int sts = 0;
                mc.GT_GetSts(slaver.prm.cardNum, slaver.prm.axisNum, out sts, 1, out clock);
                if ((sts & (1 << 10)) == 0)
                {
                    rtn += mc.GT_PrfFollow(slaver.prm.cardNum, slaver.prm.axisNum, 0);
                }
                else
                {
                    return -1;//模式不对
                }
            }
            rtn += mc.GT_SetFollowMaster(slaver.prm.cardNum, slaver.prm.axisNum, master.prm.axisNum, mc.FOLLOW_MASTER_ENCODER, 0);
            rtn += mc.GT_FollowClear(slaver.prm.cardNum, slaver.prm.axisNum, 0);
            rtn += mc.GT_FollowClear(slaver.prm.cardNum, slaver.prm.axisNum, 1);
            return rtn;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="master"></param>
        /// <param name="slaver"></param>
        /// <param name="passpos">relPasspos为true的时候 这个是增量值,relPasspos为false的时候,这个为绝对值</param>
        /// <param name="data"></param>
        /// <param name="relPasspos">是否使用增量穿越点</param>
        /// <returns></returns>
        int IMotionPart5.MC_Cam(AxisRef master, AxisRef slaver, double passpos, List<CCamData> data, bool relPasspos)
        {
            short rtn = 0;
            uint clock = 0;
            //判断是否是跟随模式
            foreach (var item in data)
            {
                rtn += mc.GT_FollowData(slaver.prm.cardNum, slaver.prm.axisNum, (int)item.master, item.slaver, (short)item.moveType, 0);
            }
            if (passpos != 0)
            {
                if (relPasspos)
                {
                    double value = 0;
                    rtn += mc.GT_GetEncPos(master.prm.cardNum, master.prm.axisNum, out value, 1, out clock);
                    rtn += mc.GT_SetFollowEvent(slaver.prm.cardNum, slaver.prm.axisNum, mc.FOLLOW_EVENT_PASS, 1, (int)(passpos + value));
                }
                else
                {
                    rtn += mc.GT_SetFollowEvent(slaver.prm.cardNum, slaver.prm.axisNum, mc.FOLLOW_EVENT_PASS, 1, (int)passpos);
                }
            }
            else
            {
                rtn += mc.GT_SetFollowEvent(slaver.prm.cardNum, slaver.prm.axisNum, mc.FOLLOW_EVENT_START, 1, (int)passpos);
            }
            rtn += mc.GT_FollowStart(slaver.prm.cardNum, 1 << (slaver.prm.axisNum - 1), 0);
            return rtn;
        }

        /// <summary>
        /// 获取指定范围的数组
        /// </summary>
        /// <param name="start"></param>
        /// <param name="lenth"></param>
        /// <returns></returns>
        public List<bool> getAllInput()
        {
            List<bool> tmp = new List<bool>();
            for (int i = 0; i < usedInput; i++)
            {
                tmp.Add(mdis[i]);
            }
            return tmp;
        }
        /// <summary>
        /// 获取指定范围的数组
        /// </summary>
        /// <param name="start"></param>
        /// <param name="lenth"></param>
        /// <returns></returns>
        public List<bool> getAllOutput()
        {
            List<bool> tmp = new List<bool>();
            for (int i = 0; i < usedOutput; i++)
            {
                tmp.Add(mdos[i]);
            }
            return tmp;
        }
        /// <summary>
        /// 获取指定范围的数组
        /// </summary>
        /// <param name="start"></param>
        /// <param name="lenth"></param>
        /// <returns></returns>
        public List<AxisRef> getAllAxis()
        {
            List<AxisRef> tmp = new List<AxisRef>();
            for (int i = 0; i < usedAxis; i++)
            {
                tmp.Add(axisRefs[i]);
            }
            return tmp;
        }
        public void Run()
        {
            IFreshModel freshMd = this as IFreshModel;
            while (true)
            {
                freshMd.Fresh();
                Thread.Sleep(1);
            }
        }
        /// <summary>
        /// 启动坐标系
        /// </summary>
        /// <param name="crdNum"></param>
        /// <returns></returns>
        int IMotionPart5.MC_CrdStart(int crdNum)
        {
            short rtn = mc.GT_CrdStart((short)AxisParameters.crdS[crdNum].cardNum, (short)AxisParameters.crdS[crdNum].crdNum, 0);
            return rtn;
        }

        int IMotionPart5.MC_CamStatus(AxisRef slaver,ref CCamStatus status)
        {
            return 0;
        }

        int IMotionPart5.MC_CrdStatus(int crdNum)
        {
            CCrdPrm prm = AxisParameters.crdS[crdNum];
            short crdRun = 0;
            int segment = 0;
            short rtn = mc.GT_CrdStatus((short)prm.cardNum, (short)prm.crdNum, out crdRun, out segment, 0);
            if (crdRun <= 0)
            {
                return 0;
            }
            else
            {
                return 1;
            }
        }

        int IMotionPart1.MC_SetPos(AxisRef axis, double pos)
        {
            mc.GT_SetPrfPos(axis.prm.cardNum, axis.prm.axisNum, (int)pos);
            mc.GT_SetEncPos(axis.prm.cardNum, axis.prm.axisNum, (int)pos);
            mc.GT_SynchAxisPos(axis.prm.cardNum, 1 << (axis.prm.axisNum - 1));
            return 0;
        }

        int IMotionPart5.MC_StartCapture(AxisRef enc, CCapturePrm capturePrm)
        {
            //CAPTURE_HOME(该宏定义为 1) Home 捕获
            //CAPTURE_INDEX(该宏定义为 2) Index 捕获
            //CAPTURE_PROBE(该宏定义为 3) 探针捕获
            //CAPTURE_HSIO0(该宏定义为 6) HSIO0 口捕获
            //CAPTURE_HSIO1(该宏定义为 7) HSIO1 口捕获
            //short rtn = mc.GT_ClrSts((short)prm.cardnum, (short)prm.axisnum, 1);
            short rtn = mc.GT_SetCaptureMode(enc.prm.cardNum, enc.prm.axisNum, (short)capturePrm.type);
            return rtn;
        }

        int IMotionPart5.MC_CaptureStatus(AxisRef enc, ref double capturePos)
        {
            short cptSts = 0;
            int value = 0;
            uint clock = 0;
            short rtn = mc.GT_GetCaptureStatus(enc.prm.cardNum, enc.prm.axisNum, out cptSts, out value, 1, out clock);
            if (rtn == 0 && cptSts == 1)
            {
                capturePos = enc.prm.pls2mm(value);
                return 0;
            }
            else
            {
                return 1;
            }
        }

        int IMotionPart5.MC_CrdCreate(int crdNum)
        {
            if (cardNum < 0 || cardNum >= AxisParameters.crdS.Count) return -1;
            short rtn = 0;
            int pmode;
            int psts;
            uint pclock;
            CCrdPrm crdPrm = AxisParameters.crdS[crdNum];
            //随便找一个轴来查看下是否是差不坐标系
            rtn += mc.GT_GetPrfMode((short)crdPrm.cardNum, (short)crdPrm.x, out pmode, 1, out pclock);
            rtn += mc.GT_GetSts((short)crdPrm.cardNum, (short)crdPrm.x, out psts, 1, out pclock);
            if ((psts & 0x400) == 0)
            {
                mc.TCrdPrm prm = new mc.TCrdPrm();
                prm.dimension = (short)crdPrm.dimension;
                prm.evenTime = 5;
                prm.setOriginFlag = 1;
                prm.synVelMax = crdPrm.getCrdMaxVelPlsPerMs();
                prm.synAccMax = crdPrm.getCrdMaxAccPlsPerMs2();
                switch (crdPrm.x)
                {
                    case 1:
                        prm.profile1 = 1;
                        break;
                    case 2:
                        prm.profile2 = 1;
                        break;
                    case 3:
                        prm.profile3 = 1;
                        break;
                    case 4:
                        prm.profile4 = 1;
                        break;
                    case 5:
                        prm.profile5 = 1;
                        break;
                    case 6:
                        prm.profile6 = 1;
                        break;
                    case 7:
                        prm.profile7 = 1;
                        break;
                    case 8:
                        prm.profile8 = 1;
                        break;
                    default:
                        break;
                }
                switch (crdPrm.y)
                {
                    case 1:
                        prm.profile1 = 2;
                        break;
                    case 2:
                        prm.profile2 = 2;
                        break;
                    case 3:
                        prm.profile3 = 2;
                        break;
                    case 4:
                        prm.profile4 = 2;
                        break;
                    case 5:
                        prm.profile5 = 2;
                        break;
                    case 6:
                        prm.profile6 = 2;
                        break;
                    case 7:
                        prm.profile7 = 2;
                        break;
                    case 8:
                        prm.profile8 = 2;
                        break;
                    default:
                        break;
                }
                switch (crdPrm.z)
                {
                    case 1:
                        prm.profile1 = 3;
                        break;
                    case 2:
                        prm.profile2 = 3;
                        break;
                    case 3:
                        prm.profile3 = 3;
                        break;
                    case 4:
                        prm.profile4 = 3;
                        break;
                    case 5:
                        prm.profile5 = 3;
                        break;
                    case 6:
                        prm.profile6 = 3;
                        break;
                    case 7:
                        prm.profile7 = 3;
                        break;
                    case 8:
                        prm.profile8 = 3;
                        break;
                    default:
                        break;
                }
                switch (crdPrm.a)
                {
                    case 1:
                        prm.profile1 = 4;
                        break;
                    case 2:
                        prm.profile2 = 4;
                        break;
                    case 3:
                        prm.profile3 = 4;
                        break;
                    case 4:
                        prm.profile4 = 4;
                        break;
                    case 5:
                        prm.profile5 = 4;
                        break;
                    case 6:
                        prm.profile6 = 4;
                        break;
                    case 7:
                        prm.profile7 = 4;
                        break;
                    case 8:
                        prm.profile8 = 4;
                        break;
                    default:
                        break;
                }
                rtn += mc.GT_SetCrdPrm((short)crdPrm.cardNum, (short)crdPrm.crdNum, ref prm);
                rtn += mc.GT_CrdClear((short)crdPrm.cardNum, (short)crdPrm.crdNum, 0);
                rtn += mc.GT_CrdClear((short)crdPrm.cardNum, (short)crdPrm.crdNum, 1);
                return rtn;
            }
            return -1;
        }

        void IFreshModel.Fresh()
        {
            subStatusData();
        }

        void IFreshModel.Run()
        {
            IFreshModel fresh = this as IFreshModel;
            while (true)
            {
                fresh.Fresh();
                Thread.Sleep(1);
            }
        }

        bool IInitModel.Init()
        {
            try
            {
                short rtn = 0;
                rtn += mc.GT_Open(cardNum, 0, 0);
                rtn += mc.GT_Reset(cardNum);
                rtn += mc.GT_LoadConfig(cardNum, @"GTS400.cfg");
                rtn += mc.GT_ClrSts(cardNum, 1, 4);
                rtn += mc.GT_ZeroPos(cardNum, 1, 4);
                if (rtn == 0)
                {
                    runThread = new System.Threading.Thread(Run);
                    runThread.IsBackground = true;
                    runThread.Start();
                    return initOk = true;
                }
                return initOk = false;
            }
            catch (Exception)
            {
                return initOk = false;
            }
        }

        bool IInitModel.UnInit()
        {
            try
            {
                //停止刷新
                if (runThread != null && runThread.IsAlive)
                {
                    runThread.Abort();
                    Thread.Sleep(5);
                    runThread = null;
                }
                short rtn = 0;
                rtn += mc.GT_Reset(cardNum);
                rtn += mc.GT_Close(cardNum);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        bool IIoPart1.getDi(int startIndex, int lenth, ref bool[] value)
        {
            throw new NotImplementedException();
        }

        bool IIoPart1.getDo(int startIndex, int lenth, ref bool[] value)
        {
            throw new NotImplementedException();
        }

        public int MC_AxisRef(int startIndex, int lenth, ref AxisRef[] axisS)
        {
            throw new NotImplementedException();
        }

        int IMotionPart5.MC_CrdSetOrigin(int crdNum, List<double> origionData)
        {
            throw new NotImplementedException();
        }

        int IMotionPart5.MC_CrdOverride(int crdNum, float BeiLv)
        {
            throw new NotImplementedException();
        }

        public int MC_HomeStatus(ref AxisRef axis)
        {
            short rtn = 0;
            mc.THomeStatus tmpHomeSts = new mc.THomeStatus();
            rtn = mc.GT_GetHomeStatus(axis.prm.cardNum, axis.prm.axisNum, out tmpHomeSts);
            if (tmpHomeSts.run == 0 && tmpHomeSts.error == mc.HOME_ERROR_NONE)//回零完成
            {
                axis.homed = 1;
                mc.GT_ZeroPos(axis.prm.cardNum, axis.prm.axisNum, 1);
                axis.isHoming = false;
            }
            if (tmpHomeSts.run == 0 && tmpHomeSts.error != mc.HOME_ERROR_NONE)//回零完成
            {
                axis.homed = -1;
                axis.isHoming = false;
            }
            return rtn;
        }
    }
}