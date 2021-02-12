//using System.ComponentModel;
//using System.Runtime.CompilerServices;
//using ByteSizeLib;

//namespace GDMENUCardManager
//{

//    public sealed class GdItem : INotifyPropertyChanged
//    {
//        private const int namemaxlen = 40;

//        public string Guid { get; set; }

//        private ByteSize _Length;
//        public ByteSize Length
//        {
//            get { return _Length; }
//            set { _Length = value; RaisePropertyChanged(); }
//        }

//        public long CdiTarget { get; set; }

//        private string _Name;
//        public string Name
//        {
//            get { return _Name; }
//            set
//            {
//                _Name = value;
//                if (_Name != null)
//                {
//                    if (_Name.Length > namemaxlen)
//                        _Name = _Name.Substring(0, namemaxlen);
//                    _Name = Helper.RemoveDiacritics(_Name).ToUpperInvariant().Trim();
//                }

//                RaisePropertyChanged();
//            }
//        }

//        private string _ImageFile;
//        public string ImageFile
//        {
//            get { return _ImageFile; }
//            set { _ImageFile = value; RaisePropertyChanged(); }
//        }

//        private string _FullFolderPath;
//        public string FullFolderPath
//        {
//            get { return _FullFolderPath; }
//            set { _FullFolderPath = value; RaisePropertyChanged(); }
//        }

//        private IpBin _Ip;
//        public IpBin Ip
//        {
//            get { return _Ip; }
//            set { _Ip = value; RaisePropertyChanged(); }
//        }

//        private int _SdNumber;
//        public int SdNumber
//        {
//            get { return _SdNumber; }
//            set { _SdNumber = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Location)); }
//        }

//        private WorkMode _Work;
//        public WorkMode Work
//        {
//            get { return _Work; }
//            set { _Work = value; RaisePropertyChanged(); }
//        }

//        public string Location
//        {
//            get { return SdNumber == 0 ? "Other" : "SD Card"; }
//        }


//        private FileFormat _FileFormat;
//        public FileFormat FileFormat
//        {
//            get { return _FileFormat; }
//            set { _FileFormat = value; RaisePropertyChanged(); }
//        }

//#if DEBUG
//        public override string ToString()
//        {
//            return $"{Location} {SdNumber} {Name}";
//        }
//#endif

//        public event PropertyChangedEventHandler PropertyChanged;

//        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
//        {
//            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
//        }
//    }

//    public enum WorkMode
//    {
//        None,
//        New,
//        Move
//    }

//    public enum FileFormat
//    {
//        Uncompressed,
//        SevenZip
//    }
//}
