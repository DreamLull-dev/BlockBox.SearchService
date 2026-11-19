using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlockBox.Models
{
    public enum SourceState
    {
        Unknown = 0,   // 从未校验
        Valid = 1,     // 源文件存在且与备份一致
        Modified = 2,  // 源文件存在但与备份不一致（备份待更新）
        Missing = 3    // 源文件不存在（只能用备份）
    }
}
