# aw-1-3 自对弈回归 + 冒烟 —— 落地说明

## 这是什么

两个黑盒测试， philosophy 不变：**只认运行效果，不看代码**。

1. **冒烟**：每次 push 自动用 Godot 无头模式启动主场景跑 600 帧，崩了 = 红。专治"编辑器好好的、换个环境就炸"。
2. **双 AI 自对弈回归**：双 AI 托管打 60 个阶段，全程断言黑盒不变量：
   - 单位列表里没有 null/已释放条目
   - 没有单位 HP ≤ 0 或 > 上限（原作 10HP 制 = 100）
   - 没有两个非运输单位叠在同一格
   - 资金不为负（字段名探测到才查）
   - 阶段推进卡死检测（默认 3000 帧没换阶段 = 卡死）

断言设计原则：**探测不到属性就跳过，绝不误报**。所以第一次跑如果全绿，先怀疑是不是没查到东西，看日志里的阶段推进记录。

## 怎么落地（4 个文件）

复制进仓库，保持相对路径：

```
tests/selfplay.gd          →  tests/selfplay.gd
tests/selfplay.tscn        →  tests/selfplay.tscn
.github/workflows/selfplay.yml  →  .github/workflows/selfplay.yml
```

提交 push 后，GitHub Actions 会多出一个"自对弈回归 + 冒烟"，和现有"编译检查"并列。

## 本地先跑一遍（强烈建议）

```bash
godot --headless --path . res://tests/selfplay.tscn -- --max-phases 30
```

- 退出码 0 = 达标，1 = 有失败，`echo $?` 看结果
- 报告写在 `user://selfplay_report.json`（Windows 一般在 `%APPDATA%/Godot/app_userdata/AWone1/`）
- 日志搜 `[SELFPLAY` 看阶段推进和失败明细

## 第一次跑大概率是红的

这不是 harness 坏了，是它第一次替你 24 小时盯对局。**红 = 它干活了**。把 `[SELFPLAY-FAIL]` 的行发回来，那就是真正的 bug 清单——比"感觉运行有问题"具体一百倍。

## 已知边界（诚实交代）

1. **遭遇战（截断）**：AI 代码里有 `encounterPause`——遭遇触发时如果弹出需要玩家裁定的 UI，自对弈会卡死并报"卡死"。这是真信号，但 v1 不会自动裁定。真遇到了再来接。
2. **伤害矩阵数字断言还没接**：现在查的是结构性不变量，没查"战车打步兵到底几滴血"。docs/damage-matrix.md 里的数值断言是 v2，需要读 Weapon.cs 的伤害接口才能接。
3. **冒烟只抓崩溃**：Godot 的脚本报错不打断退出码，冒烟管"崩/不崩"，管不住"报错但继续跑"。自对弈的断言管后者。

## 后续

- v2：接 damage-matrix 数值断言（需要 Weapon.cs 接口）
- v2：遭遇自动裁定
- 可以再加 job：每日定时跑（cron）而不是只在 push 时跑
