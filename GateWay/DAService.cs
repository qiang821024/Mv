﻿using DataService;
using Microsoft.Extensions.Logging;
using Mv.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Timers;
namespace BatchCoreService
{

    public class DriverDataContext : IDriverDataContext
    {

        public const string DRIVERS = "DRIVERS";
        private readonly IConfigureFile configure;
        public DriverDataContext(IConfigureFile configure)
        {
            this.configure = configure;
        }

        public IEnumerable<Driver> GetDrivers()
        {
            var drivers = configure.GetValue<IEnumerable<Driver>>(DRIVERS) ?? new List<Driver>();
            return drivers;
        }

        public void SetDrivers(IEnumerable<Driver> drivers)
        {
            configure.SetValue(DRIVERS, drivers);
        }
    }
    public class Driver
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Assembly { get; set; }
        public string ClassName { get; set; }
        public ICollection<Group> Groups { get; set; } = new List<Group>();
        public ICollection<DriverArgument> Arguments { get; set; } = new List<DriverArgument>();
        public bool Active { get; set; }
    }
    public class Group
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int UpdateRate { get; set; }
        public int DeadBand { get; set; }
        public bool Active { get; set; }
        public int DriverId { get; set; }
        public ICollection<TagMetaData> TagMetas { get; set; } = new List<TagMetaData>();
    }
    public class DriverArgument
    {
        public int Id { get; set; }
        public int DriverID { get; set; }
        public string PropertyName { get; set; }
        public string PropertyValue { get; set; }
    }
    public class DAService : IDataServer
    {

        const char SPLITCHAR = '.';
        const string SERVICELOGSOURCE = "DataProcess";
        //可配置参数，从XML文件读取
        int CYCLE = 60000;


        static ILogger Log;
        private readonly IDriverDataContext dataContext;
        private System.Timers.Timer timer1 = new System.Timers.Timer();

        public ITag this[short id]
        {
            get
            {
                int index = GetItemProperties(id);
                if (index >= 0)
                {
                    return this[_list[index].Name];
                }
                return null;
            }
        }

        public ITag this[string name]
        {
            get
            {
                if (string.IsNullOrEmpty(name)) return null;
                ITag dataItem;
                _mapping.TryGetValue(name.ToUpper(), out dataItem);
                return dataItem;
            }
        }

        List<TagMetaData> _list;

        public IList<TagMetaData> MetaDataList
        {
            get { return _list; }
        }

        public IList<Scaling> ScalingList
        {
            get { return _scales; }
        }

        object _syncRoot;

        public object SyncRoot
        {
            get
            {
                if (this._syncRoot == null)
                {
                    Interlocked.CompareExchange(ref this._syncRoot, new object(), null);
                }

                return this._syncRoot;
            }
        }


        Dictionary<string, ITag> _mapping;

        List<Scaling> _scales;

        SortedList<short, IDriver> _drivers;

        public IEnumerable<IDriver> Drivers
        {
            get { return _drivers.Values; }
        }

        CompareCondBySource _compare;

        readonly ExpressionEval reval;
        private readonly ILogger logger;

        public ExpressionEval Eval
        {
            get { return reval; }
        }

        private object _myLock = new object();
        Dictionary<short, string> _archiveList = null; //是否需要lock

        public Dictionary<short, string> ArchiveList
        {
            get
            {
                return _archiveList;
            }
        }

        public DAService(ILogger logger, IDriverDataContext dataContext)
        {
            Log = logger;
            this.dataContext = dataContext;
            _scales = new List<Scaling>();
            _drivers = new SortedList<short, IDriver>();
            reval = new ExpressionEval(this);

            InitServerByDatabase();
            InitConnection();

            timer1.Elapsed += timer1_Elapsed;
            timer1.Interval = CYCLE;
            timer1.Enabled = true;
            timer1.Start();
        }

        public void Dispose()
        {
            lock (this)
            {
                try
                {
                    if (timer1 != null)
                        timer1.Dispose();
                    if (_drivers != null)
                    {
                        foreach (var driver in Drivers)
                        {
                            driver.OnClose -= this.reader_OnClose;
                            driver.Dispose();
                        }
                        _mapping.Clear();
                        reval.Dispose();
                    }
                }
                catch (Exception e)
                {
                    AddErrorLog(e);
                }
            }
        }

        public void AddErrorLog(Exception e)
        {
            Log.LogError(e.GetExceptionMsg());
        }

        private void timer1_Elapsed(object sender, ElapsedEventArgs e)
        {
            foreach (IDriver d in Drivers)
            {
                if (d.IsClosed)
                {
                    d.Connect(); //t.IsAlive可加入判断；如线程异常，重新启动。
                }
            }
        }



        #region 初始化（标签数据服务器）

        void InitConnection()
        {
            foreach (IDriver reader in _drivers.Values)
            {
                reader.OnClose += new ShutdownRequestEventHandler(reader_OnClose);
                if (reader.IsClosed)
                {
                    //if (reader is IFileDriver)
                    reader.Connect();
                }

                foreach (IGroup grp in reader.Groups)
                {
                    //    grp.DataChange += new DataChangeEventHandler(grp_DataChange);
                    //可在此加入判断，如为ClientDriver发出，则变化数据毋须广播，只需归档。
                    grp.IsActive = grp.IsActive;
                }
            }

            //此处需改进,与Condition采用相同的处理方式，可配置
        }

        void InitServerByDatabase()
        {

        }
        #endregion




        void OnValueChanged(object sender, DataService.ValueChangedEventArgs e)
        {
            var tag = sender as ITag;
            //DataHelper.Instance.ExecuteStoredProcedure("AddEventLog",
            //    DataHelper.CreateParam("@StartTime", SqlDbType.DateTime, tag.TimeStamp),
            //    DataHelper.CreateParam("@Source", SqlDbType.NVarChar, tag.ID.ToString(), 50),
            //    DataHelper.CreateParam("@StartTime", SqlDbType.NVarChar, tag.ToString(), 50));
        }

        public HistoryData[] BatchRead(DataSource source, bool sync, params ITag[] itemArray)
        {
            int count = itemArray.Length;
            HistoryData[] data = new HistoryData[count];
            Dictionary<IGroup, List<ITag>> dict = new Dictionary<IGroup, List<ITag>>();
            for (int i = 0; i < count; i++)
            {
                short id = itemArray[i].ID;
                ITag tag = this[id];
                if (tag != null)
                {
                    IGroup grp = tag.Parent;
                    if (!dict.ContainsKey(grp))
                        dict.Add(grp, new List<ITag> { tag });
                    else
                        dict[grp].Add(tag);
                }
            }

            int j = 0;
            foreach (var dev in dict)
            {
                var list = dev.Value;
                var array = dev.Key.BatchRead(source, sync, list.ToArray());
                if (array == null) continue;
                Array.Copy(array, 0, data, j, array.Length);
                j += array.Length;
            }

            return data;
        }

        public int BatchWrite(Dictionary<string, object> tags, bool sync)
        {
            int rs = -1;
            Dictionary<IGroup, SortedDictionary<ITag, object>> dict =
                new Dictionary<IGroup, SortedDictionary<ITag, object>>();
            foreach (var item in tags)
            {
                var tag = this[item.Key];
                if (tag != null)
                {
                    IGroup grp = tag.Parent;
                    SortedDictionary<ITag, object> values;
                    if (!dict.ContainsKey(grp))
                    {
                        values = new SortedDictionary<ITag, object>();
                        if (tag.Address.VarType != DataType.BOOL && tag.Address.VarType != DataType.STR)
                        {
                            values.Add(tag, tag.ValueToScale(Convert.ToSingle(item.Value)));
                        }
                        else
                            values.Add(tag, item.Value);
                        dict.Add(grp, values);
                    }
                    else
                    {
                        values = dict[grp];
                        if (tag.Address.VarType != DataType.BOOL && tag.Address.VarType != DataType.STR)
                        {
                            values.Add(tag, tag.ValueToScale(Convert.ToSingle(item.Value)));
                        }
                        else
                            values.Add(tag, item.Value);
                    }
                }
                else Log.LogError(string.Format("变量{0}不在变量表中，无法下载", item.Key));
            }

            foreach (var dev in dict)
            {
                rs = dev.Key.BatchWrite(dev.Value, sync);
            }

            return rs;
        }

        public IDriver AddDriver(short id, string name, string server, int timeOut,
            string assembly, string className, string spare1, string spare2)
        {
            if (_drivers.ContainsKey(id))
                return _drivers[id];
            IDriver dv = null;
            try
            {
                Assembly ass = Assembly.LoadFrom(assembly);
                var dvType = ass.GetType(className);
                if (dvType != null)
                {
                    //dv = new ModbusDriver.ModbusTCPReader(this, id, name, server, timeOut);
                    dv = Activator.CreateInstance(dvType,
                        new object[] { this, id, name, server, timeOut, spare1, spare2 }) as IDriver;
                    if (dv != null)
                        _drivers.Add(id, dv);
                }
            }
            catch (Exception e)
            {
                AddErrorLog(e);
            }

            return dv;
        }

        public bool RemoveDriver(IDriver device)
        {
            lock (SyncRoot)
            {
                if (_drivers.Remove(device.ID))
                {
                    device.Dispose();
                    device = null;
                    return true;
                }
                return false;
            }
        }

        void reader_OnClose(object sender, ShutdownRequestEventArgs e)
        {
            Log.LogWarning(e.shutdownReason);
            //AddErrorLog(new Exception(e.shutdownReason));
        }

        public bool AddItemIndex(string key, ITag value)
        {
            key = key.ToUpper();
            if (_mapping.ContainsKey(key))
                return false;
            _mapping.Add(key, value);
            return true;
        }

        public bool RemoveItemIndex(string key)
        {
            return _mapping.Remove(key.ToUpper());
        }



        string[] itemList = null;

        public IEnumerable<string> BrowseItems(BrowseType browseType, string tagName, DataType dataType)
        {
            lock (SyncRoot)
            {
                if (_list.Count == 0) yield break;
                int len = _list.Count;
                if (itemList == null)
                {
                    itemList = new string[len];
                    for (int i = 0; i < len; i++)
                    {
                        itemList[i] = _list[i].Name;
                    }

                    Array.Sort(itemList);
                }

                int ii = 0;
                bool hasTag = !string.IsNullOrEmpty(tagName);
                bool first = true;
                string str = hasTag ? tagName + SPLITCHAR : string.Empty;
                if (hasTag)
                {
                    ii = Array.BinarySearch(itemList, tagName);
                    if (ii < 0) first = false;
                    //int strLen = str.Length;
                    ii = Array.BinarySearch(itemList, str);
                    if (ii < 0) ii = ~ii;
                }

                //while (++i < len && temp.Length >= strLen && temp.Substring(0, strLen) == str)
                do
                {
                    if (first && hasTag)
                    {
                        first = false;
                        yield return tagName;
                    }

                    string temp = itemList[ii];
                    if (hasTag && !temp.StartsWith(str, StringComparison.Ordinal))
                        break;
                    if (dataType == DataType.NONE || _mapping[temp].Address.VarType == dataType)
                    {
                        bool b3 = true;
                        if (browseType != BrowseType.Flat)
                        {
                            string curr = temp + SPLITCHAR;
                            int index = Array.BinarySearch(itemList, ii, len - ii, curr);
                            if (index < 0) index = ~index;
                            b3 = itemList[index].StartsWith(curr, StringComparison.Ordinal);
                            if (browseType == BrowseType.Leaf)
                                b3 = !b3;
                        }

                        if (b3)
                            yield return temp;
                    }
                } while (++ii < len);
            }
        }

        public int GetScaleByID(short Id)
        {
            if (_scales == null || _scales.Count == 0) return -1;
            return _scales.BinarySearch(new Scaling { ID = Id });
        }

        public IGroup GetGroupByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (IDriver device in Drivers)
            {
                foreach (IGroup grp in device.Groups)
                {
                    if (grp.Name == name)
                        return grp;
                }
            }

            return null;
        }

        public void ActiveItem(bool active, params ITag[] items)
        {
            Dictionary<IGroup, List<short>> dict = new Dictionary<IGroup, List<short>>();
            for (int i = 0; i < items.Length; i++)
            {
                List<short> list = null;
                ITag item = items[i];
                dict.TryGetValue(item.Parent, out list);
                if (list != null)
                {
                    list.Add(item.ID);
                }
                else
                    dict.Add(item.Parent, new List<short> { item.ID });

            }

            foreach (var grp in dict)
            {
                grp.Key.SetActiveState(active, grp.Value.ToArray());
            }
        }

        public int GetItemProperties(short id)
        {
            return _list.BinarySearch(new TagMetaData { ID = id });
        }

    }
}
