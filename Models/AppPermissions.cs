namespace DeptDam.Models;

public static class AppPermissions
{
    public const string AssetsView = "Assets.View";
    public const string AssetsUpload = "Assets.Upload";
    public const string AssetsEdit = "Assets.Edit";
    public const string AssetsDelete = "Assets.Delete";
    public const string AssetsDownload = "Assets.Download";
    public const string AssetsReview = "Assets.Review";
    public const string AssetsWorkflow = "Assets.Workflow";
    
    public const string CollectionsView = "Collections.View";
    public const string CollectionsManage = "Collections.Manage";
    
    public const string SettingsManage = "Settings.Manage";
    public const string UsersManage = "Users.Manage";
    public const string RolesManage = "Roles.Manage";
    public const string AuditView = "Audit.View";

    public static readonly string[] AllPermissions = new[]
    {
        AssetsView, AssetsUpload, AssetsEdit, AssetsDelete, AssetsDownload, AssetsReview, AssetsWorkflow,
        CollectionsView, CollectionsManage,
        SettingsManage, UsersManage, RolesManage, AuditView
    };

    public static List<string> GetDefaultPermissions(string roleName)
    {
        return roleName switch
        {
            "SuperAdmin" => AllPermissions.ToList(),
            "DAM_Admin" => AllPermissions.ToList(),
            "DAM_Contributor" => new List<string> 
            { 
                AssetsView, 
                AssetsUpload, 
                AssetsEdit, 
                AssetsDownload,
                CollectionsView,
                CollectionsManage
            },
            "DAM_Reviewer" => new List<string> 
            { 
                AssetsView, 
                AssetsReview, 
                AssetsDownload,
                CollectionsView
            },
            "DAM_Viewer" => new List<string> 
            { 
                AssetsView, 
                AssetsDownload,
                CollectionsView
            },
            _ => new List<string>()
        };
    }
}
