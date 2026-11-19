using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlockBox.Models
{
    public class ModeFilter
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public AppMode Mode { get; set; }              // 应用哪个模式
        public string Pattern { get; set; } = "";      // 匹配内容，比如 "Word"、"Excel"、"WPS"
    }
}
