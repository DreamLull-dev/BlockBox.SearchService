using System;

namespace BlockBox.Helpers
{
    /// <summary>
    /// 反馈/更新相关的联系方式与商店信息。发布前把这里改成你自己的信息即可。
    /// </summary>
    internal static class SupportInfo
    {
        /// <summary>反馈邮箱（也会用于“发送邮件”按钮）。</summary>
        internal const string Email = "you@example.com";

        /// <summary>项目主页 / 反馈仓库地址，留空则不显示。</summary>
        internal const string GitHubUrl = "";

        /// <summary>Microsoft Store 产品 ID（发布后在合作伙伴中心可以查到），留空则隐藏商店入口。</summary>
        internal const string StoreProductId = "";

        internal static Uri? StorePageUri =>
            string.IsNullOrWhiteSpace(StoreProductId)
                ? null
                : new Uri($"ms-windows-store://pdp/?ProductId={StoreProductId}");

        internal static Uri? StoreReviewUri =>
            string.IsNullOrWhiteSpace(StoreProductId)
                ? null
                : new Uri($"ms-windows-store://review/?ProductId={StoreProductId}");

        internal static Uri? MailtoUri
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Email)) return null;
                return new Uri($"mailto:{Email}?subject=Block%20Box%20Feedback");
            }
        }
    }
}
