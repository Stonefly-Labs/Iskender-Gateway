using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

public class MigrationScriptTests
{
    private static readonly MigrationIdentity Identity = new("alpha", 1);
    private static readonly SchemaVersionRange Range = new(1, 1);

    [Theory]
    [InlineData("a")]
    [InlineData("alpha")]
    [InlineData("alpha_2")]
    [InlineData("a234567890123456789012345678901")]
    public void AValidModuleNameIsAccepted(string name)
    {
        var module = new MigrationModule(name, Range, () => [], []);

        Assert.Equal(name, module.Name);
        Assert.Equal("isk_" + name, module.Schema);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Alpha")]
    [InlineData("1alpha")]
    [InlineData("_alpha")]
    [InlineData("al-pha")]
    [InlineData("a2345678901234567890123456789012")]
    [InlineData("alpha\n")]
    public void AnInvalidModuleNameOrDependencyIsRefused(string name)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new MigrationModule(name, Range, () => [], [])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new MigrationModule("beta", Range, () => [], [name])).Code);
    }

    [Fact]
    public void AModuleKeepsItsDependenciesAndRange()
    {
        var module = new MigrationModule("beta", new SchemaVersionRange(1, 3), () => [], ["alpha", "gamma"]);

        Assert.Equal(["alpha", "gamma"], module.DependsOn);
        Assert.Equal(new SchemaVersionRange(1, 3), module.SupportedRange);
    }

    [Theory]
    [InlineData("0001_widgets.up.sql", 1, "widgets", true)]
    [InlineData("0001_widgets.down.sql", 1, "widgets", false)]
    [InlineData("0042_add_label_2.up.sql", 42, "add_label_2", true)]
    [InlineData("9999_last.down.sql", 9999, "last", false)]
    public void AScriptNameGivesItsVersionSlugAndDirection(string name, int version, string slug, bool forward)
    {
        var file = MigrationFile.Parse(name, []);

        Assert.NotNull(file);
        Assert.Equal((version, slug, forward), (file.Version, file.Slug, file.Forward));
    }

    [Theory]
    [InlineData("0000_zero.up.sql")]
    [InlineData("1_short.up.sql")]
    [InlineData("001_three.up.sql")]
    [InlineData("00001_five.up.sql")]
    [InlineData("0001.up.sql")]
    [InlineData("0001_Widgets.up.sql")]
    [InlineData("0001_widgets.sql")]
    [InlineData("0001_widgets.UP.sql")]
    [InlineData("0001_widgets.up.sql.bak")]
    [InlineData("x001_widgets.up.sql")]
    public void AnyOtherScriptNameIsRefused(string name)
    {
        Assert.Null(MigrationFile.Parse(name, []));
        var module = new MigrationModule("alpha", Range, () => [(name, [])], []);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => module.ReadFiles()).Code);
    }

    [Fact]
    public void ABomIsRemovedAndLineEndingsBecomeLf()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a\r\nb\rc\n")];

        Assert.True(MigrationScript.TryNormalize(withBom, out var text));
        Assert.Equal("a\nb\nc\n", text);
    }

    [Fact]
    public void OnlyALeadingBomIsRemovedAndNothingElseChanges()
    {
        Assert.True(MigrationScript.TryNormalize(Encoding.UTF8.GetBytes("  x﻿\t-- note \n\n"), out var text));
        Assert.Equal("  x﻿\t-- note \n\n", text);
    }

    [Fact]
    public void TheSameTextWithDifferentLineEndingsOrABomHasOneChecksum()
    {
        var lf = Script("CREATE TABLE t ();\n", "DROP TABLE t;\n");
        var crlf = Script("CREATE TABLE t ();\r\n", "DROP TABLE t;\r\n");
        var cr = Script("﻿CREATE TABLE t ();\r", "DROP TABLE t;\r");

        Assert.Equal(lf.Checksum, crlf.Checksum);
        Assert.Equal(lf.Checksum, cr.Checksum);
        Assert.Equal("CREATE TABLE t ();\n", cr.Forward);
    }

    [Theory]
    [InlineData(new byte[] { 0xC3 })]
    [InlineData(new byte[] { 0x41, 0xFF, 0x42 })]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]
    [InlineData(new byte[] { 0xC0, 0xAF })]
    public void InvalidUtf8IsRefusedInEitherScript(byte[] content)
    {
        Assert.False(MigrationScript.TryNormalize(content, out _));
        Assert.False(MigrationScript.TryCreate(Identity, content, "ok"u8, out _));
        Assert.False(MigrationScript.TryCreate(Identity, "ok"u8, content, out _));
    }

    [Fact]
    public void TheChecksumMatchesAFixedVector()
    {
        // SHA-256 of "CREATE TABLE widgets (id text primary key);\n", 0x00, "-- iskender:data-losing\nDROP TABLE widgets;\n".
        var script = Script("CREATE TABLE widgets (id text primary key);\n", "-- iskender:data-losing\nDROP TABLE widgets;\n");

        Assert.Equal("1a2b9197394a06248d04ef387521863ef9eecf17891db4329f2b845e7c3aa445", script.Checksum);
        Assert.Equal("6e340b9cffb37a989ca544e6bb780a2c78901d3fb33738768511a30617afa01d", Script("", "").Checksum);
        Assert.Same(Identity, script.Identity);
    }

    [Fact]
    public void EditingEitherScriptChangesTheChecksum()
    {
        var original = Script("CREATE TABLE t ();\n", "DROP TABLE t;\n");

        Assert.NotEqual(original.Checksum, Script("CREATE TABLE t ();\n\n", "DROP TABLE t;\n").Checksum);
        Assert.NotEqual(original.Checksum, Script("CREATE TABLE t ();\n", "DROP TABLE  t;\n").Checksum);
        Assert.NotEqual(original.Checksum, Script("CREATE TABLE t ();\nDROP TABLE t;\n", "").Checksum);
    }

    [Theory]
    [InlineData("-- iskender:data-losing\nALTER TABLE t DROP COLUMN c;\n", true)]
    [InlineData("-- iskender:data-losing", true)]
    [InlineData("-- iskender:data-losing\r\nALTER TABLE t DROP COLUMN c;\r\n", true)]
    [InlineData("ALTER TABLE t DROP COLUMN c;\n-- iskender:data-losing\n", false)]
    [InlineData("\n-- iskender:data-losing\n", false)]
    [InlineData(" -- iskender:data-losing\n", false)]
    [InlineData("-- iskender:data-losing \n", false)]
    [InlineData("-- ISKENDER:DATA-LOSING\n", false)]
    [InlineData("DROP TABLE t;\n", false)]
    public void OnlyTheMarkerAsTheReverseScriptsFirstLineIsDataLosing(string reverse, bool dataLosing)
    {
        Assert.Equal(dataLosing, Script("SELECT 1;\n", reverse).DataLosing);
        Assert.False(Script(reverse, "SELECT 1;\n").DataLosing);
    }

    private static MigrationScript Script(string forward, string reverse)
    {
        Assert.True(MigrationScript.TryCreate(Identity, Encoding.UTF8.GetBytes(forward), Encoding.UTF8.GetBytes(reverse), out var script));
        return script;
    }
}
