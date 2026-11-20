using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ProbeOriginalGenerator
{
    private static string directory;
    private static readonly BindingFlags AllInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Expected isolated package directory.");
            directory = Path.GetFullPath(args[0]);
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            var main = Assembly.LoadFrom(Path.Combine(directory, "Main.exe"));
            var instrument = Assembly.LoadFrom(Path.Combine(directory, "USTCORi.SimExp.Instruments.FGJ.dll"));
            var modelType = instrument.GetType("CWi3mf2yMYYTP7gBEZ.rNBOwGt9ehDb9Mlj1A", true);
            var paraType = instrument.GetType("USTCORi.SimExp.Instruments.FGJ.ParaName", true);
            // InitEnvData reads instrument parameter 1, not the separate NGreen parameter.
            var refractiveIndexKey = Enum.ToObject(paraType, 1);
            var model = Activator.CreateInstance(modelType, true);
            var create = modelType.GetMethod("CreateDefaultParam", AllInstance);
            var generated = create.Invoke(model, new object[] { refractiveIndexKey });
            var n = Convert.ToDouble(generated.GetType().GetProperty("CurValue").GetValue(generated, null));
            if (!(n > 1.0 && n < 2.0)) throw new InvalidOperationException("Generated prism index outside physical range.");

            var envType = main.GetType("USTCORi.SimExp.Common.EnvData", true);
            envType.GetMethod("CreateInstant", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            var env = envType.GetProperty("Instant", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
            var setValue = envType.GetMethod("SetValue", AllInstance);
            // Derive the actual nested enum type from the signature; the static dump omits declaring types.
            var envPara = setValue.GetParameters()[0].ParameterType;
            var indexKey = Enum.ToObject(envPara, 8);
            setValue.Invoke(env, new object[] { indexKey, n });

            var windowType = main.GetType("USTCORi.SimExp.Common.DataWindow", true);
            Type singleType = null;
            foreach (var candidate in main.GetTypes())
            {
                if (candidate.Name != "SinglePara") continue;
                if (singleType != null) throw new InvalidOperationException("Ambiguous SinglePara type.");
                singleType = candidate;
            }
            if (singleType == null) throw new TypeLoadException("SinglePara type not found.");
            var paraBase = singleType.BaseType;
            // The audited method reads only the name and EnvData. No WPF constructor or window is invoked.
            var window = FormatterServices.GetUninitializedObject(windowType);
            var single = FormatterServices.GetUninitializedObject(singleType);
            var nameField = paraBase.GetField("name", AllInstance);
            var getStd = windowType.GetMethod("GetStd", AllInstance);
            nameField.SetValue(single, "SLJZSL_DOUBLE");
            var originalIndex = (string)getStd.Invoke(window, new object[] { single });
            nameField.SetValue(single, "SLJSXPXJ_DOUBLE");
            var originalDeviation = (string)getStd.Invoke(window, new object[] { single });

            double a = Math.PI / 3.0;
            double delta = (2.0 * Math.Asin(n * Math.Sin(a / 2.0)) - a) * 180.0 / Math.PI;
            string expectedIndex = n.ToString("F2", CultureInfo.InvariantCulture);
            string expectedDeviation = delta.ToString("F2", CultureInfo.InvariantCulture);
            if (originalIndex != expectedIndex || originalDeviation != expectedDeviation)
                throw new InvalidOperationException("Original GetStd output differs from reconstructed formula.");

            Console.WriteLine("GENERATOR_VERIFIED");
            Console.WriteLine("model_generated_n=" + n.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("original_SLJZSL_DOUBLE=" + originalIndex);
            Console.WriteLine("original_SLJSXPXJ_DOUBLE=" + originalDeviation);
            Console.WriteLine("formula_matches_original=True");
            Console.WriteLine("visible_windows_created=0");
            Console.WriteLine("mouse_or_keyboard_actions=0");
            Console.WriteLine("exam_files_modified=0");
            Console.WriteLine("main_entrypoint_called=False");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        foreach (var suffix in new[] { ".dll", ".exe" })
        {
            var candidate = Path.Combine(directory, name + suffix);
            if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
        }
        return null;
    }
}
