using CommunityToolkit.Mvvm.ComponentModel;

namespace NotiFlow.Models
{
    /// <summary>
    /// 角色伴随挂件预设数据模型。
    /// </summary>
    public partial class CharacterPresetItemDto : ObservableObject
    {
        public string Id { get; set; } = "none";
        public string Name { get; set; } = "";
        public string ImagePath { get; set; } = "";

        [ObservableProperty]
        private bool _isSelected;

        public bool IsNone => Id == "none";
        public bool IsCustom => Id == "custom";
    }
}
