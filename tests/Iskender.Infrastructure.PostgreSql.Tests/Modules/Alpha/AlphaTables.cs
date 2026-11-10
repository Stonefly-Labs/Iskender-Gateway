namespace Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;

/// <summary>The alpha module's table descriptors, declared once (research R6).</summary>
internal static class AlphaTables
{
    public static readonly TenantTable Widgets = new("alpha", "widgets");

    public static readonly TenantTable WidgetNotes = new("alpha", "widget_notes");

    public static readonly OperatorTable Settings = new("alpha", "settings");
}
