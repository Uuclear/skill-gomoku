# Tasks

- [x] Task 1: EffectPlayer 扩展——类别色彩脉冲 + 玩家卡光环
  - [x] SubTask 1.1: 在 EffectPlayer 中新增按技能类别着色的脉冲接口（复用 SkillBarView 类别配色常量：Info 蓝 / Buff 绿 / Debuff 红 / Board 金 / Special 紫），保留原己/敌色脉冲用于普通落子
  - [x] SubTask 1.2: 新增玩家卡光环特效接口 `CardPulse(bool own, Color color)`：在指定玩家卡位置播一次类别色扩散环（复用现有程序化圆环 sprite 与 life 循环模式）
  - [x] SubTask 1.3: 在 BattleController 中改造技能事件处理：有落点技能按类别色播 Pulse；无落点技能按类别调用 CardPulse（Buff→释放方卡，Debuff/干扰→受影响方卡）；普通落子保持原行为

- [x] Task 2: 技能释放横幅
  - [x] SubTask 2.1: 在 EffectPlayer（或新建小型组件）实现 `SkillBanner(string skillName, bool own, SkillCat cat)`：屏幕中上部（避开 toast、棋盘与技能栏的安全区域）显示横幅，含技能名 +"我方/对方"标识，背景按类别着色，约 1.6s 渐隐，连续释放时替换或排队不堆叠遮挡
  - [x] SubTask 2.2: 在 BattleController 技能生效事件处统一调用 SkillBanner（双方释放均触发）

- [x] Task 3: 炸弹爆发特效 + 震屏
  - [x] SubTask 3.1: 在 EffectPlayer 实现 `Burst(x, y)`：落点播至少两层递进扩散环（不同初速/尺寸/寿命）
  - [x] SubTask 3.2: 实现短促震屏（对棋盘根节点做 0.2s 内小幅随机位移后归位，不累积偏移）
  - [x] SubTask 3.3: BattleController 在技能 4 生效时调用 Burst + 震屏

- [x] Task 4: UI 重叠修复
  - [x] SubTask 4.1: toast 位置上移，与表情按钮栏（430..510）错开，并复查与横幅位置不冲突
  - [x] SubTask 4.2: 己方表情气泡上移，避开选目标条区域（-724..-668），对方气泡位置复查不受影响
  - [x] SubTask 4.3: HudView 玩家卡昵称区与能量区锚点去交叠（消除 0.62..0.65 交叠带），昵称超长时以省略号/缩字号兜底，能量数字完整可读
  - [x] SubTask 4.4: SkillBarView 技能槽 cost 标签收缩至左上局部区域（不再满铺全槽），与双行技能名（含"剩X次"）保持间距

- [x] Task 5: 编译与走查验证
  - [x] SubTask 5.1: Unity 编译无错误无新警告（编辑器批量编译或命令行 batchmode 编译验证）
  - [x] SubTask 5.2: 走查战斗场景：横幅/类别色脉冲/卡光环/炸弹爆发/震屏均可见且不遮挡操作；四处重叠点在 1080×1920 下不再重叠

# Task Dependencies
- [Task 2] 依赖 [Task 1]（横幅与特效共用 EffectPlayer 层与 life 循环，先扩载体）
- [Task 3] 依赖 [Task 1]（Burst 复用脉冲层）
- [Task 4] 与 [Task 1]/[Task 2]/[Task 3] 可并行（不同文件为主；BattleController 改动需协调，先特效后布局）
- [Task 5] 依赖 [Task 1] [Task 2] [Task 3] [Task 4]
