using System.Reflection;
using System.Text.RegularExpressions;
using HIS.BuildingBlocks.Database;
using Xunit;

namespace HIS.ArchitectureTests;

public class ArchitectureGateTests
{
    [Fact]
    public void Gate01_Modules_MustNotDirectlyReferenceEachOther()
    {
        // Check project dependencies between the 13 server modules
        var moduleNames = new[]
        {
            "HIS.Modules.Platform", "HIS.Modules.Config", "HIS.Modules.Emr", "HIS.Modules.Patient",
            "HIS.Modules.Outpatient", "HIS.Modules.Inpatient", "HIS.Modules.Ordering", "HIS.Modules.MedTech",
            "HIS.Modules.Pharmacy", "HIS.Modules.Inventory", "HIS.Modules.Finance", "HIS.Modules.Integration",
            "HIS.Modules.Operations"
        };

        var solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        foreach (var m1 in moduleNames)
        {
            var csproj = Path.Combine(solutionDir, "src", "Server", m1, $"{m1}.csproj");
            if (!File.Exists(csproj)) continue;

            var content = File.ReadAllText(csproj);
            foreach (var m2 in moduleNames)
            {
                if (m1 == m2) continue;
                // Assert no ProjectReference to other modules
                Assert.DoesNotContain($"ProjectReference Include=\"..\\{m2}\\", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain($"ProjectReference Include=\"..\\..\\Server\\{m2}\\", content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Gate01_ClientModels_MustNotReferenceAvaloniaUiControls()
    {
        var modelBaseType = typeof(HIS.Client.Core.ClientModelBase);
        var assembly = modelBaseType.Assembly;
        var refs = assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(refs, r => r.Name?.StartsWith("Avalonia.Controls", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(refs, r => r.Name?.StartsWith("Ursa.Controls", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Gate08_DatabasePureStorage_ZeroBusinessLogicAndZeroProprietaryTypes()
    {
        var ddl = DbSchemaInitializer.DdlScript;

        // 1. Prohibited SQL constructs in DB
        var prohibitedKeywords = new[]
        {
            "PROCEDURE", "TRIGGER", "FOREIGN KEY", "REFERENCES",
            "CHECK (", "WHERE ...", "CREATE VIEW", "ROWVERSION",
            "UNIQUEIDENTIFIER", "DATETIMEOFFSET"
        };

        foreach (var kw in prohibitedKeywords)
        {
            Assert.False(
                Regex.IsMatch(ddl, $@"\b{Regex.Escape(kw)}\b", RegexOptions.IgnoreCase),
                $"Database purity violation: Prohibited SQL construct '{kw}' was found in database DDL script!");
        }
    }
}

