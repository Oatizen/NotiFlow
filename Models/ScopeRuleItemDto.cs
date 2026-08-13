namespace NotiFlow.Models
{
    /// <summary>
    /// 浣滅敤鍩熻鍒欐潯鐩紝鐢ㄤ簬搴忓垪鍖栨寔涔呭寲鍜?UI 灞曠ず銆?    /// 鍚屾椂鏈嶅姟浜庛€岄€氱煡鏉ユ簮銆嶅拰銆岀敓鏁堝満鏅€嶄袱涓淮搴︾殑杩囨护鍒楄〃銆?    /// </summary>
    public class ScopeRuleItemDto
    {
        /// <summary>
        /// 鍙嬪ソ鏄剧ず鍚嶇О锛堝 "寰俊"銆?PowerPoint"锛夈€?        /// 涓昏渚?UI 鍒楄〃灞曠ず浣跨敤锛屼笉鍙備笌鍖归厤鍒ゅ畾銆?        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// 鍞竴鏍囪瘑绗︼紝鐢ㄤ簬瀹為檯鐨勮繃婊ゅ尮閰嶃€?        /// 鐢熸晥鍦烘櫙缁村害锛氳繘绋嬪彲鎵ц鏂囦欢鍚嶏紙濡?"powerpnt.exe"锛?        /// 閫氱煡鏉ユ簮缁村害锛氬簲鐢ㄧ殑 AUMID 鎴?AppName锛堝 "Microsoft.Windows.Defender_xxx"锛?        /// </summary>
        public string Identifier { get; set; } = "";

        /// <summary>
        /// 缂撳瓨璇ュ簲鐢ㄨ繎鏈熺殑閫氱煡鏂囨湰鍐呭锛岀敤浜庡湪鐣岄潰灞曞紑棰勮
        /// </summary>
        public System.Collections.Generic.List<string> RecentMessages { get; set; } = new();

        /// <summary>
        /// 该作用域独立的弹幕样式覆盖配置。若为空，则代表使用全局样式。
        /// </summary>
        public BarrageConfigDto? StyleOverride { get; set; }
    }
}
