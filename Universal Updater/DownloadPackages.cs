﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Universal_Updater
{
    class DownloadPackages
    {
        static Tuple<DateTime, long, long> DownloadingProgress = new Tuple<DateTime, long, long>(DateTime.MinValue, 0, 0);
        static string[] installedPackages = File.ReadAllLines(@"C:\ProgramData\Universal Updater\InstalledPackages.csv");
        static List<string> filteredPackages = new List<string>();
        static bool isFeatureInstalled = false;
        static bool filterCBSPackagesOnly = false;
        static readonly string[] knownPackages = { "ms_commsenhancementglobal.mainos", "ms_commsmessagingglobal.mainos", "microsoftphonefm.platformmanifest.efiesp", "microsoftphonefm.platformmanifest.mainos", "microsoftphonefm.platformmanifest.updateos", "UserInstallableFM.PlatformManifest" };
        static readonly string[] featurePackages = { "MS_RCS_FEATURE_PACK.MainOS.cbsr", "ms_projecta.mainos" };
        static readonly string[] skippedPackages = { };
        static readonly string[] packageCBSExtension = { ".cab", ".cbs_" };
        static readonly string[] packageSPKGExtension = { ".spkg", ".spkg_" };
        static Uri downloadFile;

        public static string[] getExtensionsList()
        {
            if (!filterCBSPackagesOnly)
            {
                return packageSPKGExtension;
            }
            return packageCBSExtension;
        }

        public static bool OnlineUpdate(string updateBuild)
        {
            // For pre-built in packages currently check for both CBS or SPKG,
            // those suppose to be sorted already for SPGK or CBS
            var targetExtensionList = packageCBSExtension.Concat(packageSPKGExtension).ToArray();

            // Get file content only once
            var targetListFile = Program.GetResourceFile($"{updateBuild}.txt").Split('\n');
            for (int i = 1; i < installedPackages.Length; i++)
            {
                for (int j = 0; j < targetExtensionList.Length; j++)
                {
                    var requiredPackages = targetListFile.Where(k => k.IndexOf(installedPackages[i].Split(',')[1] + targetExtensionList[j], StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                    if (requiredPackages != null)
                    {
                        if (requiredPackages.IndexOf("ms_projecta.mainos", StringComparison.OrdinalIgnoreCase) >= 0 || requiredPackages.IndexOf("MS_RCS_FEATURE_PACK.MainOS.cbsr", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isFeatureInstalled = true;
                        }
                        if (!shouldSkip(requiredPackages, true))
                        {
                            filteredPackages.Add(requiredPackages);
                        }
                    }
                }
            }
            for (int i = 0; i < knownPackages.Length; i++)
            {
                if (string.Join("\n", filteredPackages).IndexOf(knownPackages[i], StringComparison.OrdinalIgnoreCase) < 0)
                {
                    var knownPackage = targetListFile.Where(j => j.IndexOf(knownPackages[i], StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                    if (knownPackage != null)
                    {
                        if (!shouldSkip(knownPackage, true))
                        {
                            filteredPackages.Add(knownPackage);
                        }
                    }
                }
            }
            return isFeatureInstalled;
        }

        public static bool OfflineUpdate(string[] folderFiles, string pushFeature)
        {
        filterPackages:
            filteredPackages.Clear();
            var folderHasCBS = false;
            var folderHasSPKG = false;
            for (int i = 0; i < packageCBSExtension.Length; i++)
            {
                var testPackages = folderFiles.Where(k => k.IndexOf(packageCBSExtension[i], StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                if (testPackages != null)
                {
                    folderHasCBS = true;
                    break;
                }
            }
            for (int i = 0; i < packageSPKGExtension.Length; i++)
            {
                var testPackages = folderFiles.Where(k => k.IndexOf(packageSPKGExtension[i], StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                if (testPackages != null)
                {
                    folderHasSPKG = true;
                    break;
                }
            }

            // We show this question only if folder has mixed packages
            if (folderHasCBS && folderHasSPKG)
            {
                Program.Write("CBS", ConsoleColor.Blue);
                Program.Write(" and ", ConsoleColor.DarkYellow);
                Program.Write("SPKG", ConsoleColor.Blue);
                Program.WriteLine(" packages detected\nEnsure to use the correct type\nUsually SPKG used for WP8, CBS for W10M", ConsoleColor.DarkYellow);
                Program.WriteLine("1. Use CBS");
                Program.WriteLine("2. Use SPKG");
                Program.Write("Choice: ", ConsoleColor.Magenta);
                ConsoleKeyInfo packagesType;
                do
                {
                    packagesType = Console.ReadKey(true);
                }
                while (packagesType.KeyChar != '1' && packagesType.KeyChar != '2');
                Console.Write(packagesType.KeyChar.ToString() + "\n");
                filterCBSPackagesOnly = packagesType.KeyChar == '1';
            }
            else
            {
                // Fall to what exists
                filterCBSPackagesOnly = folderHasCBS;
            }

            // Allow user to push all package if he want
            Program.WriteLine("\nFilter packages options: ", ConsoleColor.Blue);
            Program.WriteLine("1. Filter packages", ConsoleColor.Green);
            Program.WriteLine("2. Include all", ConsoleColor.DarkYellow);
            Program.Write("Choice: ", ConsoleColor.Magenta);
            ConsoleKeyInfo packagesFilterAction;
            do
            {
                packagesFilterAction = Console.ReadKey(true);
            }
            while (packagesFilterAction.KeyChar != '1' && packagesFilterAction.KeyChar != '2');
            Console.Write(packagesFilterAction.KeyChar.ToString() + "\n");

            var targetExtensionList = getExtensionsList();
            var previewType = "cbs";
            if (!filterCBSPackagesOnly)
            {
                previewType = "spkg";
            }

            if (packagesFilterAction.KeyChar == '1')
            {
                Program.WriteLine($"\nFiltering packages (type: {previewType}), please wait...", ConsoleColor.DarkGray);

                for (int i = 0; i < installedPackages.Length; i++)
                {
                    for (int j = 0; j < targetExtensionList.Length; j++)
                    {
                        var checkName = installedPackages[i].Split(',')[1] + targetExtensionList[j];
                        var requiredPackages = folderFiles.Where(k => k.IndexOf(checkName, StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                        if (requiredPackages != null)
                        {
                            if (!shouldSkip(requiredPackages))
                            {
                                filteredPackages.Add(requiredPackages);
                            }
                        }
                    }
                }
                for (int i = 0; i < knownPackages.Length; i++)
                {
                    if (string.Join("\n", filteredPackages).IndexOf(knownPackages[i], StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        var knownPackage = folderFiles.Where(j => j.IndexOf(knownPackages[i], StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                        if (knownPackage != null)
                        {
                            bool skipThisPackage = false;
                            if (filterCBSPackagesOnly)
                            {
                                foreach (var spkgExt in packageSPKGExtension)
                                {
                                    if (knownPackage.IndexOf(spkgExt, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        skipThisPackage = true;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                if (knownPackage.IndexOf(".cbs_", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    skipThisPackage = true;
                                    break;
                                }
                            }
                            if (!skipThisPackage && !shouldSkip(knownPackage))
                            {
                                filteredPackages.Add(knownPackage);
                            }
                        }
                    }
                }
                if (pushFeature == "Y")
                {
                    for (int i = 0; i < featurePackages.Length; i++)
                    {
                        var requiredPackages = folderFiles.Where(j => j.IndexOf(featurePackages[i], StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                        if (requiredPackages != null)
                        {
                            filteredPackages.Add(requiredPackages);
                        }
                    }
                }
            }
            else
            {
                Program.WriteLine($"\nAdding packages (type: {previewType}), please wait...", ConsoleColor.DarkGray);
                for (int j = 0; j < targetExtensionList.Length; j++)
                {
                    var requiredPackages = folderFiles.Where(k => k.IndexOf(targetExtensionList[j], StringComparison.OrdinalIgnoreCase) >= 0);
                    if (requiredPackages != null)
                    {
                        foreach (var requiredPackage in requiredPackages)
                        {
                            bool skipThisPackage = false;
                            if (filterCBSPackagesOnly)
                            {
                                foreach (var spkgExt in packageSPKGExtension)
                                {
                                    if (requiredPackage.IndexOf(spkgExt, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        skipThisPackage = true;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                if (requiredPackage.IndexOf(".cbs_", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    skipThisPackage = true;
                                    break;
                                }
                            }
                            if (!skipThisPackage && !shouldSkip(requiredPackage))
                            {
                                filteredPackages.Add(requiredPackage);
                            }
                        }
                    }
                }
            }

            if (filteredPackages.Count > 0)
            {
                var testCabFile = filteredPackages.FirstOrDefault();
                var certificateIssuer = "";
                var certificateDate = "";
                var certificateExpireDate = "";
                var dtFmt = "";
                bool isTestSigned = false;
                try
                {
                    X509Certificate cert = X509Certificate.CreateFromSignedFile(testCabFile);

                    certificateDate = cert.GetEffectiveDateString();
                    certificateExpireDate = cert.GetExpirationDateString();
                    certificateIssuer = cert.Issuer;
                    Match m = Regex.Match(certificateIssuer, @"CN=(.*?)(?=,)");
                    if (m.Success)
                    {
                        certificateIssuer = m.Groups[1].Value;
                    }

                    Program.WriteLine("\n[CERTIFICATE]", ConsoleColor.Green);
                    Program.Write("Issuer : ", ConsoleColor.DarkGray);
                    Program.WriteLine(certificateIssuer, ConsoleColor.DarkCyan);
                    Program.Write("Date   : ", ConsoleColor.DarkGray);
                    Program.Write(certificateDate.ToDate(ref dtFmt).Value.ToString("d"), ConsoleColor.DarkGray);
                    Program.Write(" - ", ConsoleColor.DarkGray);
                    Program.WriteLine(certificateExpireDate.ToDate(ref dtFmt).Value.ToString("d"), ConsoleColor.DarkGray);
                    Program.Write("Type   : ", ConsoleColor.DarkGray);

                    // Currently we do basic check using [Issuer]
                    // Test signed mostly contain `Development` or `Test`
                    var hasDevelopmentKeyword = certificateIssuer.IndexOf("Development", StringComparison.OrdinalIgnoreCase) >= 0;
                    var hasTestKeyword = certificateIssuer.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0;
                    if ((hasDevelopmentKeyword || hasTestKeyword))
                    {
                        Program.Write("Test signed", ConsoleColor.DarkYellow);
                        Program.WriteLine(" (Please double check)", ConsoleColor.DarkGray);
                        isTestSigned = true;
                    }
                    else
                    {
                        Program.Write("Production signed", ConsoleColor.Green);
                        Program.WriteLine(" (Please double check)", ConsoleColor.DarkGray);
                    }
                }
                catch (Exception ex)
                {
                }

                // If we got the certificate date then just use it
                DateTime? formatedDate = null;

                var expectedDateFormat = "";
                var dateModifiedString = "";
                try
                {
                    // Not sure how this works, but tested on Windows 11 and seems fine
                    var shellAppType = Type.GetTypeFromProgID("Shell.Application");
                    dynamic shellApp = Activator.CreateInstance(shellAppType);
                    var cabFolder = shellApp.NameSpace(testCabFile);

                    foreach (var item in cabFolder.Items())
                    {
                        dateModifiedString = (string)cabFolder.GetDetailsOf(item, 3);
                        // Fix possible encoding crap
                        byte[] bytes = Encoding.Default.GetBytes(dateModifiedString);
                        dateModifiedString = Encoding.UTF8.GetString(bytes).Replace("?", "");
                        formatedDate = dateModifiedString.ToDate(ref expectedDateFormat);
                        break;
                    }
                }
                catch (Exception ex)
                {
                }


                if (formatedDate != null && formatedDate.HasValue)
                {
                    var dateOnly = formatedDate.Value.Date;
                    Program.Write("Files  : " + dateOnly.ToString("d"), ConsoleColor.DarkGray);
                    var datePlusTwoDays = dateOnly.AddDays(2);
                    Program.WriteLine($" ({dtFmt})", ConsoleColor.DarkGray);

                    if (isTestSigned)
                    {
                        // Show this red label if we expect that packages are test signed
                        Program.Write("\n[IMPORTANT] ", ConsoleColor.DarkRed);
                        Program.Write("\nPackages expected to be ", ConsoleColor.DarkYellow);
                        Program.WriteLine("(Test Signed)", ConsoleColor.Blue);
                        Program.WriteLine("Set your device to the required date", ConsoleColor.DarkYellow);
                    }
                }
                else
                {
                    Program.WriteLine("\nCould not parse date: " + dateModifiedString, ConsoleColor.DarkGray);
                    Program.WriteLine("(Ignore this if packages are not test signed)", ConsoleColor.DarkGray);
                }

                Program.Write("\nTotal expected packages: ", ConsoleColor.DarkGray);
                Program.WriteLine(filteredPackages.Count.ToString(), ConsoleColor.DarkYellow);
                Program.WriteLine("1. Push packages", ConsoleColor.Green);
                Program.WriteLine("2. Retry");
                Program.Write("Choice: ", ConsoleColor.Magenta);
                ConsoleKeyInfo packagesAction;
                do
                {
                    packagesAction = Console.ReadKey(true);
                }
                while (packagesAction.KeyChar != '1' && packagesAction.KeyChar != '2');
                Console.Write(packagesAction.KeyChar.ToString() + "\n");

                if (packagesAction.KeyChar == '1')
                {
                    for (int i = 0; i < filteredPackages.Where(j => !string.IsNullOrWhiteSpace(j)).Count(); i++)
                    {
                        Program.WriteLine($@"[{i + 1}/{filteredPackages.Where(j => !string.IsNullOrWhiteSpace(j)).Count()}] {filteredPackages[i].Split('\\').Last()}", ConsoleColor.DarkGray);
                        File.Copy(filteredPackages[i], $@"{Environment.CurrentDirectory}\{GetDeviceInfo.SerialNumber[0]}\Packages\{filteredPackages[i].Split('\\').Last()}", true);
                    }
                }
                else
                {
                    Program.WriteLine("");
                    goto filterPackages;
                }
            }
            else
            {
                Program.WriteLine("\nNo packages detected that match with your system!", ConsoleColor.Red);
                Program.WriteLine("1. Retry");
                Program.WriteLine("2. Exit");
                Program.Write("Choice: ", ConsoleColor.Magenta);
                ConsoleKeyInfo packagesAction;
                do
                {
                    packagesAction = Console.ReadKey(true);
                }
                while (packagesAction.KeyChar != '1' && packagesAction.KeyChar != '2');
                Console.Write(packagesAction.KeyChar.ToString() + "\n");
                if (packagesAction.KeyChar == '1')
                {
                    Program.WriteLine("");
                    goto filterPackages;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        public static async Task<bool> DownloadUpdate(string update)
        {
            WebClient client = new WebClient();
            Process downloadProcess = new Process();
            downloadProcess.StartInfo.FileName = @"C:\ProgramData\Universal Updater\wget.exe";
            downloadProcess.StartInfo.UseShellExecute = false;
            for (int i = 0; i < filteredPackages.Where(j => !string.IsNullOrWhiteSpace(j)).Count(); i++)
            {
                downloadFile = new Uri(filteredPackages[i]);
                Console.WriteLine($@"[{i + 1}/{filteredPackages.Where(j => !string.IsNullOrWhiteSpace(j)).Count()}] {downloadFile.LocalPath.Split('/').Last()}");
                if (update == "15254.603")
                {
                    downloadProcess.StartInfo.Arguments = $@"{downloadFile} --spider";
                    downloadProcess.StartInfo.RedirectStandardOutput = true;
                    downloadProcess.StartInfo.RedirectStandardError = true;
                    downloadProcess.StartInfo.RedirectStandardInput = true;
                    downloadProcess.Start();
                    downloadProcess.WaitForExit();
                    var logOutput = (await downloadProcess.StandardError.ReadToEndAsync()).Split('\n');
                    var fileSize = Convert.ToInt64(logOutput.Where(j => j.IndexOf("Length:", StringComparison.OrdinalIgnoreCase) >= 0).ToArray()[0].Split(' ')[1]);
                    do
                    {
                        downloadProcess.StartInfo.Arguments = $@"-q -c -P ""{Environment.CurrentDirectory}\{GetDeviceInfo.SerialNumber[0]}\Packages"" {downloadFile} --no-check-certificate --show-progress";
                        downloadProcess.StartInfo.RedirectStandardOutput = false;
                        downloadProcess.StartInfo.RedirectStandardError = false;
                        downloadProcess.StartInfo.RedirectStandardInput = false;
                        downloadProcess.Start();
                        Console.Title = "Universal Updater.exe";
                        downloadProcess.WaitForExit();
                    }
                    while (fileSize != new FileInfo($@"{Environment.CurrentDirectory}\{GetDeviceInfo.SerialNumber[0]}\Packages\{downloadFile.LocalPath.Split('/').Last()}").Length);
                }
                else
                {
                    var fileStream = client.OpenRead(downloadFile);
                    var fileSize = Convert.ToInt64(client.ResponseHeaders["Content-Length"]);
                    fileStream.Close();
                    do
                    {
                        client.DownloadProgressChanged += DownloadProgressChanged;
                        await client.DownloadFileTaskAsync(downloadFile, $@"{Environment.CurrentDirectory}\{GetDeviceInfo.SerialNumber[0]}\Packages\{downloadFile.LocalPath.Split('/').Last()}");
                        Console.Title = "Universal Updater.exe";
                    }
                    while (fileSize != new FileInfo($@"{Environment.CurrentDirectory}\{GetDeviceInfo.SerialNumber[0]}\Packages\{downloadFile.LocalPath.Split('/').Last()}").Length);
                }
            }

            return true;
        }

        private static void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs downloadProgressChangedEventArgs)
        {
            DownloadingProgress = new Tuple<DateTime, long, long>(DateTime.Now, downloadProgressChangedEventArgs.TotalBytesToReceive, downloadProgressChangedEventArgs.BytesReceived);
            Console.Title = $"Universal Updater.exe  [Downloading: {downloadFile.LocalPath.Split('/').Last().Remove(28)}... {((DownloadingProgress.Item3 * 100) / DownloadingProgress.Item2)}% - {DownloadingProgress.Item3}/{DownloadingProgress.Item2}]";
        }
        public static bool shouldSkip(string package, bool onlinePackage = false)
        {
            foreach (var testCheck in skippedPackages)
            {
                if (package.IndexOf(testCheck, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    // Package exists in the skip list
                    return true;
                }
            }

            if (!onlinePackage)
            {
                FileInfo fileInfo = new FileInfo(package);
                long sizeInBytes = fileInfo.Length;

                if (sizeInBytes <= 0)
                {
                    // Package has 0MB size, not a valid package
                    return true;
                }
            }

            if (filteredPackages.Contains(package))
            {
                // Package already added, this happens due to extension and prefix match
                return true;
            }

            return false;
        }
    }
    public static class Extensions
    {
        public static DateTime? ToDate(this string dateTimeStr, ref string fmtOut)
        {
            const DateTimeStyles style = DateTimeStyles.AllowWhiteSpaces;

            DateTime? result = null;
            CultureInfo currentCulture = CultureInfo.CurrentCulture;
            fmtOut = currentCulture.DateTimeFormat.ShortDatePattern;
            result = DateTime.TryParse(dateTimeStr, CultureInfo.InvariantCulture,
                          style, out var dt) ? dt : null as DateTime?;
            return result;
        }
    }
}