//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

namespace DiscUtils.Iso9660
{
    internal class BuildParameters
    {
        public BuildParameters()
        {
            VolumeIdentifier = string.Empty;
            VolumeSetIdentifier = string.Empty;
            PublisherIdentifier = string.Empty;
            DataPreparerIdentifier = string.Empty;
            ApplicationIdentifier = string.Empty;
            UseJoliet = true;
        }

        public bool UseJoliet
        {
            get;
            set;
        }

        public string VolumeIdentifier { get; set; }
        public string SystemIdentifier { get; set; }
        public string VolumeSetIdentifier { get; set; }
        public string PublisherIdentifier { get; set; }
        public string DataPreparerIdentifier { get; set; }
        public string ApplicationIdentifier { get; set; }

        public uint LBAoffset
        {
            get;
            set;
        }

        public uint LastFileStartSector
        {
            get;
            set;
        }

        public uint? EndSector
        {
            get;
            set;
        }
    }
}
