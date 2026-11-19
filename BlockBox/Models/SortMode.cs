using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlockBox.Models
{
    public enum SortMode
    {
        ByName = 0,       // 按名称
        ByCreatedTime,    // 按加入时间
        Custom            // 自定义（按 SortOrder）
    }
}
