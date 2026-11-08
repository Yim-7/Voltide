# 发布到你自己的 GitHub 仓库（操作手册）

> 前提：本项目源自 [sdparklab/Voltide](https://github.com/sdparklab/Voltide)，采用 MIT 许可证。
> 你可以改名、修改并发布，但 **`LICENSE` 必须保留**（含原作者 Joko 的版权声明）。

## 第 1 步：改名（可选，想保留 Voltide 名字可跳过）

在项目目录打开 PowerShell：

```powershell
.\rebrand.ps1 -NewName 你的软件名 -Author "你的署名"
```

脚本会更新界面标题、托盘、程序集信息、输出文件名；不会改动 `LICENSE`。

## 第 2 步：重新编译并确认

```powershell
.\build.ps1
```

看到 GUI / Test 两个输出、无报错即通过。想消除 SmartScreen 可再执行 `.\sign.ps1`。

## 第 3 步：在 GitHub 网页创建空仓库

1. 登录 GitHub → 右上角 **+** → **New repository**。
2. Repository name 填你的软件名；选择 **Public**（公开）或 **Private**。
3. **不要**勾选 "Add a README"、".gitignore"、"license"（本地已有，避免冲突）。
4. 创建后复制仓库地址，形如：
   `https://github.com/你的用户名/你的仓库名.git`

## 第 4 步：替换远程地址并提交

```powershell
# 移除指向原作者的旧远程（你没有推送权限）
git remote remove origin

# 换成你自己的仓库地址
git remote add origin https://github.com/你的用户名/你的仓库名.git

# 暂存并提交全部改动
git add .
git commit -m "Rebrand and add custom device wizard, T7/X7 battery support"

# 推送到 GitHub（首次加 -u 建立跟踪）
git branch -M main
git push -u origin main
```

弹出登录时，用 GitHub 账号或 Personal Access Token 完成认证。

## 第 5 步（可选）：发布可下载版本

`*.exe` 已被 `.gitignore` 排除，仓库里只放源码。想让别人直接下载：

1. GitHub 仓库页 → **Releases** → **Draft a new release**。
2. 填写 Tag（如 `v1.0.0`）、标题，把编译好的 exe 拖到附件区 → **Publish release**。

## 合规检查清单

- [ ] `LICENSE` 原样保留，含 `Copyright (c) 2026 Joko`
- [ ] README 注明基于 Voltide（MIT）并附原仓库链接
- [ ] 你自己新增的功能（通用添加设备向导、T7/X7 适配）在 README 中说明
- [ ] 若改名，确认界面/文件中没有遗漏的旧品牌（可用 `git grep -i voltide` 自查）

## 备注

- `devices.json` 当前含 T7/X7 规则，会作为示例配置一起提交；如不想公开可加入 `.gitignore`，或改名为 `devices.example.json`。
- 仓库默认不含 `Fonts/`（`.gitignore` 已排除），不影响他人按 `build.ps1` 自行编译，运行时会回退系统字体。
