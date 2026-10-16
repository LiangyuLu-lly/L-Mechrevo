# Git 小抄 —— L-Mechrevo 专用

> ⭐ 只列**你会用到的**命令。默认在 ⭐ `D:\openLM\` 里执行（⭐ 这就是仓库根目录 ✅）。
> ⚠️ 全程 ⭐ **本地**：⛔ 没有远程仓库、⛔ 不联网、⛔ 不上传任何东西。

---

## 0. 两条线是什么

| 分支 | 版本 | 干什么 |
|---|---|---|
| ⭐ `main` | **0.291.8.1** | ⭐ 按机主反馈**修 bug**，⭐ 随时能打包 |
| ⭐ `baolongda` | **0.291.9 / beta22.9** | ⭐ **宝龙达适配**（蛟龙16Pro 22/23/24） |

---

## 1. 我要看现在在哪条线上

```powershell
cd D:\openLM
git branch            # 带 * 的就是当前分支
git status            # 看有没有没保存的改动
```

## 2. ⭐ 切换线（最常用）

```powershell
git checkout main         # 切到 22.8 修 bug 线
git checkout baolongda    # 切到宝龙达适配线
```

⭐ **切换是安全的**：没提交的改动如果会冲突，git 会**拒绝切换并告诉你**（⛔ 不会偷偷弄丢）。

## 3. ⭐ 存一版（提交）—— 每完成一小块就做

```powershell
cd D:\openLM
git add -A                          # 把改动都放进来
git commit -m "一句话说清这次改了什么"   # 存一版
```

⭐ **提交说明怎么写**（照这个来，将来才看得懂）：
```
改了什么：修了 XXX 机型的 YYY
为什么：机主报 ZZZ
影响：只有 AAA 路径变了，BBB 没动
```

## 4. ⭐ 我要出包（打包）

```powershell
git branch --show-current     # ① 先确认在正确的线上！
git status                    # ② 确认没有没提交的改动（应为 clean）
# ③ 然后按 installer-src\打包须知.txt 走 build / publish / merge / ISCC
```

⚠️ **打包前务必确认分支** —— 在 `baolongda` 上打出来的包是 **0.291.9**，⛔ 不能当 22.8 的修复版发。

## 5. ⭐ 我给某条线打好了一个包，想记下来

```powershell
git tag -a v0.291.8.2 -m "0.291.8.2 发布：修了 XXX"
git tag                       # 列出所有标签
```

## 6. 我看历史 / 看某次改了什么

```powershell
git log --oneline             # 一行一条历史（最近的在最上面）
git log --oneline --all       # 两条线的都看
git show <那次的编号>          # 看那一次具体改了哪些行
```

## 7. ⭐ 22.8 修了 bug，想让适配线也拿到（挑拣）

```powershell
git checkout baolongda
git cherry-pick <main 上那次提交的编号>
```

## 8. ⭐⭐ 适配做完了，要合并回主线

```powershell
git checkout main
git merge baolongda
```

- ✅ 能自动合的地方 git 自己合
- ⚠️ **冲突会明确标出来**（看起来像这样）：
  ```
  <<<<<<< HEAD
  版本号 = 0.291.8.1
  =======
  版本号 = 0.291.9
  >>>>>>> baolongda
  ```
  ⭐ 你把不要的那几行删掉、留下想要的，然后 `git add -A` + `git commit` 即可。
  ⚠️ **版本号这类冲突必须人决定** —— ⭐ 合并后应该改成**新的发布版本号**（例如 0.291.9）。

⭐⭐ **合并错了能撤销**（这就是比手工复制强的地方）：
```powershell
git merge --abort                      # 合并过程中后悔
git reset --hard HEAD~1                # 已经合完想撤销（⚠️ 会丢未提交改动）
```

## 9. 我不想要某个改动了

```powershell
git checkout -- 文件路径      # 把**没提交**的某个文件恢复原样
git restore 文件路径          # 同上（新版写法）
```

## 10. 出事了想回到某个包的状态

```powershell
git log --oneline             # 找到那次的编号或标签
git checkout v0.291.8.1       # 直接看那个标签的状态（⚠️ 只读，别在上面改）
git checkout main             # 看完切回来
```

---

## ⚠️ 三条铁律

1. ⭐ **切分支前先 `git status`** —— 有没提交的改动就先提交（⭐ 或先确认可以丢 ✅）。
2. ⭐ **打包前先 `git branch --show-current`** —— 在错的分支上打包是**最容易犯的错** ⚠️。
3. ⭐ **每完成一小块就提交** —— 攒一大坨再提交，将来合并会痛苦得多 ⚠️。

## 📌 什么不进版本控制（`.gitignore` 已定）

```
⛔ obj/ bin/ rebuild/ installer-src/Output/    —— 编译与打包产物（能重建）
⛔ dotnet-sdk/ tools/ghidra/ tools 的下载物与发布产物 —— 下载来的，不是我们的代码
⛔ installer-src/files/GCU/payload/            —— GCU 厂商载荷（≈67 MB）
⛔ lm_pkg_files/ backup/ archive/ work/        —— 依赖/备份/存档/临时
✅ source/ docs/ installer-src/*.iss 与除 payload 外的 files/ 修改日志.txt 更新日志.txt   —— 这些要进去
```

⭐ 判断标准一句话：**「这个文件是我们手写的吗？」** 是 ⇒ 进；否（生成/下载/备份）⇒ 不进。
