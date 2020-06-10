using System.ComponentModel;
using System.Runtime.CompilerServices;
using ByteSizeLib;

namespace GDMENUCardManager
{

    public sealed class GdItem : INotifyPropertyChanged
    {
        private const int namemaxlen = 40;

        public string Guid { get; set; }

        public ByteSize Length { get; set; }

        public long CdiTarget { get; set; }

        private string _Name;
        public string Name
        {
            get { return _Name; }
            set
            {
                _Name = value;
                if (_Name != null)
                {
                    if (_Name.Length > namemaxlen)
                        _Name = _Name.Substring(0, namemaxlen);
                    _Name = Helper.RemoveDiacritics(_Name).ToUpperInvariant().Trim();
                }

                RaisePropertyChanged();
            }
        }

        private string _ImageFile;
        public string ImageFile
        {
            get { return _ImageFile; }
            set { _ImageFile = value; RaisePropertyChanged(); }
        }

        private string _FullFolderPath;
        public string FullFolderPath
        {
            get { return _FullFolderPath; }
            set { _FullFolderPath = value; RaisePropertyChanged(); }
        }

        public IpBin Ip { get; set; }

        private int _SdNumber;
        public int SdNumber
        {
            get { return _SdNumber; }
            set { _SdNumber = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Location)); }
        }

        private WorkMode _Work;
        public WorkMode Work
        {
            get { return _Work; }
            set { _Work = value; RaisePropertyChanged(); }
        }

        public string Location
        {
            get { return SdNumber == 0 ? "Other" : "SD Card"; }
        }

#if DEBUG
        public override string ToString()
        {
            return $"{Location} {SdNumber} {Name}";
        }
#endif

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum WorkMode
    {
        None,
        New,
        Move
    }
}
