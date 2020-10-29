﻿namespace MotionWrapper
{
    /// <summary>
    /// 刷新模块 
    /// </summary>
    public interface IFreshable
    {
        void Fresh();                    //刷新状态
        void Run();                      //运行中
    }
}