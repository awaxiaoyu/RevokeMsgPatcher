using RevokeMsgPatcher.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RevokeMsgPatcher.Matcher
{
    public class ModifyFinder
    {
        // TODO 该逻辑需要优化！
        public static List<Change> FindChanges(string path, List<ReplacePattern> replacePatterns)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            // 读取整个文件(dll)
            byte[] fileByteArray = File.ReadAllBytes(path);
            Console.WriteLine("读取文件耗时：{0}ms.", sw.Elapsed.TotalMilliseconds);

            List<Change> changes = new List<Change>(); // 匹配且需要替换的地方

            // 查找所有替换点。部分 QQNT 版本可能已经安装过旧群聊补丁，
            // 游戏/客户端版本更新后刷新特征时，允许已替换的旧点跳过并继续补剩余撤回路径。
            SortedSet<string> alreadyReplaced = new SortedSet<string>();
            SortedSet<string> missing = new SortedSet<string>();
            foreach (ReplacePattern pattern in replacePatterns)
            {
                // 所有的匹配点位
                int[] matchIndexs = FuzzyMatcher.MatchAll(fileByteArray, pattern.Search);
                int[] replaceMatchIndexs = FuzzyMatcher.MatchAll(fileByteArray, pattern.Replace);
                Console.WriteLine("匹配{0}耗时：{1}ms.", pattern.Category, sw.Elapsed.TotalMilliseconds);
                if (matchIndexs.Length >= 1)
                {
                    for (int i = 0; i < matchIndexs.Length; i++)
                    {
                        // 与要替换的串不一样才需要替换（当前的特征肯定不一样）
                        if (!FuzzyMatcher.IsEqual(fileByteArray, matchIndexs[i], pattern.Replace))
                        {
                            changes.Add(new Change(matchIndexs[i], pattern.Replace));
                        }
                    }
                }
                else if (replaceMatchIndexs.Length > 0)
                {
                    alreadyReplaced.Add(pattern.Category);
                }
                else
                {
                    missing.Add(pattern.Category);
                }
            }

            if (missing.Count == 0)
            {
                if (changes.Count == 0)
                {
                    throw new BusinessException("match_already_replace", "特征比对：当前应用已经安装了所选功能补丁！");
                }

                return changes;
            }

            // 有特征既找不到 Search 也找不到 Replace，说明当前版本需要重新定位对应路径。
            if (missing.Count > 0)
            {
                Tuple<bool, SortedSet<string>> res = IsAllReplaced(fileByteArray, replacePatterns);
                if (res.Item1)
                {
                    throw new BusinessException("match_already_replace", "特征比对：当前应用已经安装了对应功能的补丁！");
                }
                else
                {
                    if (alreadyReplaced.Count > 0 && changes.Count > 0 && missing.Count == 0)
                    {
                        return changes;
                    }
                    else if (res.Item2.Count > 0)
                    {
                        throw new BusinessException("match_inconformity", $"特征比对：以下功能补丁已经安装，请取消勾选！\n已安装功能：【{string.Join("、", res.Item2)}】");
                    }
                    else
                    {
                        int resolvedPatternNum = replacePatterns.Count - missing.Count;
                        throw new BusinessException("match_inconformity", $"特征比对：当前特征码匹配数[{resolvedPatternNum}]和期望的匹配数[{replacePatterns.Count}]不一致。\n" +
                            $"出现此种情况的一般有如下可能：\n" +
                            $"1. 你可能已经安装了某个功能的补丁，请选择未安装功能进行安装。\n" +
                            $"2. 如果当前版本为最新版本，特征码可能出现变化（可能性比较低），请联系作者处理。");
                    }
                }
            }

            throw new BusinessException("match_inconformity", "特征比对：当前特征码状态异常，请重新检测文件版本和补丁配置。");
        }

        public static SortedSet<string> FindReplacedFunction(string path, List<ReplacePattern> replacePatterns)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            byte[] fileByteArray = File.ReadAllBytes(path);
            Console.WriteLine("读取文件耗时：{0}ms.", sw.Elapsed.TotalMilliseconds);
            Tuple<bool, SortedSet<string>> res = IsAllReplaced(fileByteArray, replacePatterns);
            Console.WriteLine("匹配耗时：{0}ms.", sw.Elapsed.TotalMilliseconds);
            return res.Item2;
        }

        private static Tuple<bool, SortedSet<string>> IsAllReplaced(byte[] partByteArray, List<ReplacePattern> replacePatterns)
        {
            Dictionary<string, int> categoryCount = new Dictionary<string, int>();
            Dictionary<string, int> categoryReplacedCount = new Dictionary<string, int>();
            SortedSet<string> alreadyReplaced = new SortedSet<string>(); // 已经被替换特征的功能
            foreach (ReplacePattern pattern in replacePatterns)
            {
                string category = pattern.Category ?? "";
                if (!categoryCount.ContainsKey(category))
                {
                    categoryCount[category] = 0;
                    categoryReplacedCount[category] = 0;
                }
                categoryCount[category]++;

                int[] searchMatchIndexs = FuzzyMatcher.MatchAll(partByteArray, pattern.Search);
                int[] replaceMatchIndexs = FuzzyMatcher.MatchAll(partByteArray, pattern.Replace);
                // 查找串没有，但是替换串存在，也就是说明这个功能已经完全完成替换
                if (searchMatchIndexs.Length == 0 && replaceMatchIndexs.Length > 0)
                {
                    categoryReplacedCount[category]++;
                }
            }

            foreach (KeyValuePair<string, int> pair in categoryCount)
            {
                if (categoryReplacedCount[pair.Key] == pair.Value)
                {
                    alreadyReplaced.Add(pair.Key);
                }
            }

            bool allReplaced = categoryCount.Count > 0 && categoryCount.All(pair => categoryReplacedCount[pair.Key] == pair.Value);
            return new Tuple<bool, SortedSet<string>>(allReplaced, alreadyReplaced);
        }
    }
}
