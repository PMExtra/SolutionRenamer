using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace SolutionRenamer
{
    public static class Program
    {
        public static void CopyAll(DirectoryInfo source, DirectoryInfo target)
        {
            Directory.CreateDirectory(target.FullName);

            foreach (var srcFile in source.GetFiles())
            {
                var targetPath = Path.Combine(target.FullName, srcFile.Name);
                Console.WriteLine($@"Copying {targetPath}");
                srcFile.CopyTo(targetPath, true);
            }

            foreach (var subDir in source.GetDirectories())
            {
                var subTarget = target.CreateSubdirectory(subDir.Name);
                CopyAll(subDir, subTarget);
            }
        }

        public static void Main(string[] args)
        {
            Console.Title = "SolutionRenamer";

            //加载配置
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location))
                .AddJsonFile("Config.json");

            var configuration = builder.Build();

            var ignorePatterns = configuration.GetSection("Ignores").GetChildren().Select(x => x.Value.TrimEnd('*').Replace("/", @"\")).ToArray();
            var fileExtensions = configuration["FileExtension"];

            var filter = fileExtensions.Split(',');

            const string oldCompanyName = "MyCompanyName";
            const string oldProjectName = "AbpZeroTemplate";

            Console.WriteLine("Input your new company name:");
            var newCompanyName = Console.ReadLine();

            Console.WriteLine("Input your new project name(NewAbpZeroTemplate):");
            var newProjectName = Console.ReadLine();
            if (string.IsNullOrEmpty(newProjectName)) newProjectName = "NewAbpZeroTemplate";

            Console.WriteLine("Output folder:");
            var rootDir = Console.ReadLine()?.Trim('"') ?? "";
            rootDir = Path.GetFullPath(rootDir).TrimEnd('\\');

            Console.WriteLine("Git cleaning...");
            Process.Start("git", "clean -Xdf")?.WaitForExit();

            Console.WriteLine("Copying files...");
            CopyAll(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "aspnet-core")), new DirectoryInfo(Path.Combine(rootDir, "aspnet-core")));
            CopyAll(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "angular")), new DirectoryInfo(Path.Combine(rootDir, "angular")));

            Console.WriteLine("Renaming...");

            var pathIgnores = ignorePatterns.Select(pattern =>
            {
                pattern = $@"^{rootDir}\{pattern.TrimEnd('\\')}"
                              .Replace(@"\", @"\\")
                              .Replace(@"**\\", @"(?:[^\\]+\\)*")
                              .Replace(@"*\\", @"[^\\]+\\")
                              .Replace("*.", @"[^\\]*.")
                              .Replace(".", @"\.")
                          + @"(?:\\.*)?$";
                return new Regex(pattern, RegexOptions.Compiled);
            }).ToArray();

            var fileIgnores = ignorePatterns.Where(pattern => !pattern.EndsWith(@"\")).Select(pattern =>
            {
                pattern = $@"^{rootDir}\{pattern}$"
                    .Replace(@"\", @"\\")
                    .Replace(@"**\\", @"(?:[^\\]+\\)*")
                    .Replace(@"*\\", @"[^\\]+\\")
                    .Replace("*.", @"[^\\]*.")
                    .Replace(".", @"\.");
                return new Regex(pattern, RegexOptions.Compiled);
            }).ToArray();

            var sp = new Stopwatch();

            sp.Start();
            RenameAllDir(rootDir, oldCompanyName, oldProjectName, newCompanyName, newProjectName, pathIgnores);
            sp.Stop();
            var spDir = sp.ElapsedMilliseconds;
            Console.WriteLine("Directory rename complete! spend:" + sp.ElapsedMilliseconds);

            sp.Reset();
            sp.Start();
            RenameAllFileNameAndContent(rootDir, oldCompanyName, oldProjectName, newCompanyName, newProjectName, pathIgnores, fileIgnores, filter);
            sp.Stop();
            var spFile = sp.ElapsedMilliseconds;
            Console.WriteLine("Filename and content rename complete! spend:" + sp.ElapsedMilliseconds);

            Console.WriteLine("");
            Console.WriteLine("=====================================Report=====================================");
            Console.WriteLine($"Processing spend time,directories:{spDir},files:{spFile}");
            Console.ReadKey();
        }

        #region 递归重命名所有目录

        /// <summary>
        ///     递归重命名所有目录
        /// </summary>
        private static void RenameAllDir(string rootDir, string oldCompanyName, string oldProjectName,
            string newCompanyName, string newProjectName, Regex[] ignores)
        {
            var allDir = Directory.GetDirectories(rootDir);

            foreach (var item in allDir)
            {
                if (ignores.Any(ignore => ignore.IsMatch(item))) continue;

                RenameAllDir(item, oldCompanyName, oldProjectName, newCompanyName, newProjectName, ignores);

                var directoryInfo = new DirectoryInfo(item);
                if (directoryInfo.Name.Contains(oldCompanyName) || directoryInfo.Name.Contains(oldProjectName))
                {
                    var newName = directoryInfo.Name;

                    if (!string.IsNullOrEmpty(oldCompanyName))
                    {
                        newName = newName.Replace(oldCompanyName, newCompanyName);
                    }
                    newName = newName.Replace(oldProjectName, newProjectName);

                    var newPath = Path.Combine(rootDir, newName);

                    if (directoryInfo.FullName != newPath)
                    {
                        Console.WriteLine(directoryInfo.FullName);
                        Console.WriteLine("->");
                        Console.WriteLine(newPath);
                        Console.WriteLine("-------------------------------------------------------------");
                        directoryInfo.MoveTo(newPath);
                    }
                }
            }
        }

        #endregion

        #region 递归重命名所有文件名和文件内容

        /// <summary>
        ///     递归重命名所有文件名和文件内容
        /// </summary>
        private static void RenameAllFileNameAndContent(string rootDir, string oldCompanyName, string oldProjectName,
            string newCompanyName, string newProjectName, Regex[] pathIgnores, Regex[] fileIgnores, string[] filter)
        {
            if (pathIgnores.Any(ignore => ignore.IsMatch(rootDir))) return;

            //获取当前目录所有指定文件扩展名的文件
            var files = new DirectoryInfo(rootDir).GetFiles().Where(m => filter.Any(f => f == m.Extension)).ToList();

            //重命名当前目录文件和文件内容
            foreach (var item in files)
            {
                if (fileIgnores.Any(ignore => ignore.IsMatch(item.FullName))) continue;

                var text = File.ReadAllText(item.FullName, Encoding.UTF8);
                text = text.Replace(oldCompanyName, newCompanyName);

                text = text.Replace(oldProjectName, newProjectName);
                if (item.Name.Contains(oldCompanyName) || item.Name.Contains(oldProjectName))
                {
                    var newName = item.Name;

                    if (!string.IsNullOrEmpty(oldCompanyName))
                    {
                        newName = newName.Replace(oldCompanyName, newCompanyName);
                    }
                    newName = newName.Replace(oldProjectName, newProjectName);
                    Debug.Assert(item.DirectoryName != null, "item.DirectoryName != null");
                    var newFullName = Path.Combine(item.DirectoryName, newName);
                    File.WriteAllText(newFullName, text, new UTF8Encoding(false));
                    if (newFullName != item.FullName) File.Delete(item.FullName);
                }
                else
                {
                    File.WriteAllText(item.FullName, text, new UTF8Encoding(false));
                }

                Console.WriteLine(item.Name + " process complete!");
            }

            //获取子目录
            var dirs = Directory.GetDirectories(rootDir);
            foreach (var dir in dirs)
            {
                RenameAllFileNameAndContent(dir, oldCompanyName, oldProjectName, newCompanyName, newProjectName, pathIgnores, fileIgnores, filter);
            }
        }

        #endregion
    }
}
