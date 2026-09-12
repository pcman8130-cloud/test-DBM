"""
DungeonVM 밸런스 라이브 대시보드 (Streamlit).

슬라이더/표로 밸런스 파라미터를 조정하고 "시뮬레이션 실행"을 누르면 DungeonVM.Simulator를
그 값으로 재실행해서 승률·티어 채택률 그래프를 그 자리에서 보여준다. run_logs.jsonl을 업로드해
사후 분석하는 기존 정적 뷰어와 달리, 여기서는 값을 바꾸는 즉시 재시뮬레이션이 돈다.

스키마는 tools/balance_pipeline/balance_schema.py(엑셀 파이프라인과 동일한 정의)를 그대로 재사용한다.
BalanceData.cs에 필드가 추가되면 balance_schema.py의 SCALAR_FIELDS만 갱신하면 이 대시보드에도 자동 반영된다.

실행:
    pip install -r requirements.txt
    streamlit run app.py
"""

from __future__ import annotations

import json
import subprocess
import sys
import time
from collections import defaultdict
from pathlib import Path

import pandas as pd
import plotly.express as px
import streamlit as st

BACKEND_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(BACKEND_ROOT / "tools" / "balance_pipeline"))
from balance_schema import ARMOR_RARITIES, SCALAR_FIELDS, WEAPON_TYPES  # noqa: E402

DEFAULT_BALANCE_JSON = BACKEND_ROOT / "DungeonVM.Core" / "Balance" / "DefaultBalance.json"
SIMULATOR_PROJECT = BACKEND_ROOT / "DungeonVM.Simulator" / "DungeonVM.Simulator.csproj"

SCRATCH_DIR = Path(__file__).resolve().parent / ".scratch"
SCRATCH_DIR.mkdir(exist_ok=True)
SCRATCH_BALANCE_JSON = SCRATCH_DIR / "current_balance.json"
SCRATCH_SUMMARY_JSON = SCRATCH_DIR / "last_summary.json"

SECTION_LABELS = {
    "Weapons": "무기 - 성장 배율",
    "Armor": "방어구 - 회피 상한",
    "VendingMachine": "자판기",
    "Wave": "웨이브 / 몬스터",
    "MetaProgression": "영혼 스킬트리",
    "Currency": "재화",
    "StageLoop": "스테이지 클리어 보상",
    "Character": "캐릭터",
    "Combat": "전투 - 속성 상성",
    "MergeGrid": "머지 그리드",
    "Rune": "속성 룬",
    "CharacterSlots": "캐릭터 슬롯",
}

BOT_ORDER = ["SpaceExpansion", "VendingRush", "MidTierCamp"]
TIER_COLORS = px.colors.sequential.Blues
RARITY_ORDER = ARMOR_RARITIES

st.set_page_config(page_title="DungeonVM 밸런스 대시보드", layout="wide")


# ---------------------------------------------------------------------------
# 상태 초기화
# ---------------------------------------------------------------------------

def _camel(pascal: str) -> str:
    return pascal[0].lower() + pascal[1:] if pascal else pascal


def load_default() -> dict:
    return json.loads(DEFAULT_BALANCE_JSON.read_text(encoding="utf-8"))


if "balance" not in st.session_state:
    st.session_state.balance = load_default()
if "summary" not in st.session_state:
    st.session_state.summary = None
if "last_run_info" not in st.session_state:
    st.session_state.last_run_info = None
if "last_error" not in st.session_state:
    st.session_state.last_error = None
if "reset_nonce" not in st.session_state:
    st.session_state.reset_nonce = 0


def widget_key(name: str) -> str:
    return f"{name}__{st.session_state.reset_nonce}"


def get_scalar(section: str, field: str):
    return st.session_state.balance.get(_camel(section), {}).get(_camel(field))


# ---------------------------------------------------------------------------
# 슬라이더 범위 추정 (필드 이름/현재값 기반 휴리스틱 — 필요하면 여기만 손보면 됨)
# ---------------------------------------------------------------------------

def slider_range(field) -> tuple[float, float, float]:
    name = field.field
    value = get_scalar(field.section, field.field)

    if name in ("MaxTier", "MaxLevel", "MaxUpgradeLevel"):
        return 1, 10, 1
    if name == "MaxStage":
        return 5, 100, 1
    if "Chance" in name or "Ratio" in name or "ClampMax" in name:
        return 0.0, 1.0, 0.01
    if "Divisor" in name:
        return 1, 10, 1

    if field.value_type is int:
        hi = max(10, int(value * 3) if value else 10)
        return 0, hi, 1

    hi = max(value * 3, value + 10) if value else 10.0
    step = 0.01 if hi <= 2 else (0.1 if hi <= 20 else 1.0)
    return 0.0, float(hi), step


def render_scalar_field(field) -> float | int:
    lo, hi, step = slider_range(field)
    value = get_scalar(field.section, field.field)
    value = field.value_type(value) if value is not None else lo

    return st.slider(
        f"{field.field} — {field.description_kr}",
        min_value=field.value_type(lo),
        max_value=field.value_type(hi),
        value=min(max(value, field.value_type(lo)), field.value_type(hi)),
        step=field.value_type(step),
        key=widget_key(f"scalar_{field.key}"),
    )


# ---------------------------------------------------------------------------
# 표 형태 편집기 (무기/방어구/비용 리스트)
# ---------------------------------------------------------------------------

def render_weapons_table() -> pd.DataFrame:
    table = st.session_state.balance.get("weapons", {}).get("table", {})
    rows = [
        {
            "Type": wt,
            "Row": table.get(wt, {}).get("row", "Front"),
            "BaseDamage": table.get(wt, {}).get("baseDamage", 0.0),
            "AttacksPerSecond": table.get(wt, {}).get("attacksPerSecond", 0.0),
            "BonusHealth": table.get(wt, {}).get("bonusHealth", 0.0),
            "PullAggro": table.get(wt, {}).get("pullAggro", 0.0),
        }
        for wt in WEAPON_TYPES
    ]
    df = pd.DataFrame(rows)
    return st.data_editor(
        df,
        key=widget_key("weapons_table"),
        hide_index=True,
        width="stretch",
        disabled=["Type"],
        column_config={"Row": st.column_config.SelectboxColumn(options=["Front", "Back"])},
    )


def render_armor_roll_ranges_table() -> pd.DataFrame:
    ranges = st.session_state.balance.get("armor", {}).get("rollRanges", {})
    rows = [
        {
            "Rarity": r,
            "MinHp": ranges.get(r, {}).get("minHp", 0.0),
            "MaxHp": ranges.get(r, {}).get("maxHp", 0.0),
            "MinAtk": ranges.get(r, {}).get("minAtk", 0.0),
            "MaxAtk": ranges.get(r, {}).get("maxAtk", 0.0),
            "MinDodge": ranges.get(r, {}).get("minDodge", 0.0),
            "MaxDodge": ranges.get(r, {}).get("maxDodge", 0.0),
        }
        for r in ARMOR_RARITIES
    ]
    df = pd.DataFrame(rows)
    return st.data_editor(df, key=widget_key("armor_ranges_table"), hide_index=True, width="stretch", disabled=["Rarity"])


def render_armor_market_values_table() -> pd.DataFrame:
    values = st.session_state.balance.get("currency", {}).get("armorMarketValues", {})
    rows = [{"Rarity": r, "Value": values.get(r, 0)} for r in ARMOR_RARITIES]
    df = pd.DataFrame(rows)
    return st.data_editor(df, key=widget_key("armor_values_table"), hide_index=True, width="stretch", disabled=["Rarity"])


def render_cost_list_table(label: str, key: str, id_col: str, current: list[int], id_offset: int = 1) -> pd.DataFrame:
    rows = [{id_col: i + id_offset, "GoldCost": cost} for i, cost in enumerate(current)]
    df = pd.DataFrame(rows)
    st.caption(label)
    return st.data_editor(df, key=widget_key(key), hide_index=True, width="stretch", disabled=[id_col])


def render_float_list_table(label: str, key: str, id_col: str, value_col: str, current: list[float]) -> pd.DataFrame:
    rows = [{id_col: i + 1, value_col: value} for i, value in enumerate(current)]
    df = pd.DataFrame(rows)
    st.caption(label)
    return st.data_editor(df, key=widget_key(key), hide_index=True, width="stretch", disabled=[id_col])


# ---------------------------------------------------------------------------
# 폼 -> BalanceData JSON 딕셔너리로 조립
# ---------------------------------------------------------------------------

def weapons_df_to_table(df: pd.DataFrame) -> dict:
    return {
        row.Type: {
            "row": row.Row,
            "baseDamage": float(row.BaseDamage),
            "attacksPerSecond": float(row.AttacksPerSecond),
            "bonusHealth": float(row.BonusHealth),
            "pullAggro": float(row.PullAggro),
        }
        for row in df.itertuples()
    }


def armor_ranges_df_to_dict(df: pd.DataFrame) -> dict:
    return {
        row.Rarity: {
            "minHp": float(row.MinHp), "maxHp": float(row.MaxHp),
            "minAtk": float(row.MinAtk), "maxAtk": float(row.MaxAtk),
            "minDodge": float(row.MinDodge), "maxDodge": float(row.MaxDodge),
        }
        for row in df.itertuples()
    }


def armor_values_df_to_dict(df: pd.DataFrame) -> dict:
    return {row.Rarity: int(row.Value) for row in df.itertuples()}


def cost_list_df_to_list(df: pd.DataFrame, id_col: str) -> list[int]:
    ordered = df.sort_values(id_col)
    return [int(v) for v in ordered["GoldCost"].tolist()]


def float_list_df_to_list(df: pd.DataFrame, id_col: str, value_col: str) -> list[float]:
    ordered = df.sort_values(id_col)
    return [float(v) for v in ordered[value_col].tolist()]


# ---------------------------------------------------------------------------
# 사이드바: 실행 옵션 / 리셋 / 저장
# ---------------------------------------------------------------------------

st.sidebar.title("실행 옵션")
runs_per_bot = st.sidebar.select_slider("봇당 실행 횟수", options=[100, 300, 500, 1000, 2000], value=500)

if st.sidebar.button("기본값으로 리셋"):
    st.session_state.balance = load_default()
    st.session_state.reset_nonce += 1
    st.session_state.summary = None
    st.rerun()

st.sidebar.download_button(
    "현재 값 JSON 다운로드",
    data=json.dumps(st.session_state.balance, ensure_ascii=False, indent=2),
    file_name="balance_override.json",
    mime="application/json",
)

st.sidebar.divider()
confirm_overwrite = st.sidebar.checkbox("DefaultBalance.json 덮어쓰기 확인")
if st.sidebar.button("DefaultBalance.json에 반영", disabled=not confirm_overwrite):
    DEFAULT_BALANCE_JSON.write_text(json.dumps(st.session_state.balance, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    st.sidebar.success("DefaultBalance.json에 저장했습니다. git diff로 확인 후 커밋하세요.")

st.title("DungeonVM 밸런스 라이브 대시보드")
st.caption("값을 조정하고 아래 '시뮬레이션 실행'을 누르면 봇 3종 시뮬레이션을 다시 돌려 그래프를 갱신합니다.")


# ---------------------------------------------------------------------------
# 메인 폼
# ---------------------------------------------------------------------------

grouped: dict[str, list] = defaultdict(list)
for f in SCALAR_FIELDS:
    grouped[f.section].append(f)

with st.form("balance_form"):
    tab_names = ["무기", "방어구", *[SECTION_LABELS[s] for s in grouped if s not in ("Weapons", "Armor")]]
    tabs = st.tabs(tab_names)

    edited_scalars: dict[str, float | int] = {}

    with tabs[0]:
        st.markdown("**무기별 기초 스탯**")
        weapons_df = render_weapons_table()
        st.markdown("**레벨별 성장 배율** (1레벨=1.0 기준 — 10레벨을 실질 엔드스펙, 15레벨을 극단적 하이롤로 설계)")
        level_multipliers_df = render_float_list_table(
            "레벨별 데미지/힐 배율", "level_multipliers", "Level", "Multiplier",
            st.session_state.balance.get("weapons", {}).get("levelMultipliers", []),
        )
        st.markdown("**공용 파라미터**")
        for f in grouped["Weapons"]:
            edited_scalars[f.key] = render_scalar_field(f)

    with tabs[1]:
        st.markdown("**방어구 등급별 랜덤 스탯 범위**")
        armor_ranges_df = render_armor_roll_ranges_table()
        st.markdown("**방어구 등급별 판매 시세**")
        armor_values_df = render_armor_market_values_table()
        st.markdown("**공용 파라미터**")
        for f in grouped["Armor"]:
            edited_scalars[f.key] = render_scalar_field(f)

    other_sections = [s for s in grouped if s not in ("Weapons", "Armor")]
    vm_upgrade_df = None
    merge_unlock_df = None
    char_slot_costs_df = None
    for tab, section in zip(tabs[2:], other_sections):
        with tab:
            for f in grouped[section]:
                edited_scalars[f.key] = render_scalar_field(f)
            if section == "VendingMachine":
                vm_upgrade_df = render_cost_list_table(
                    "레벨별 업그레이드 골드 비용", "vm_upgrade_costs", "Level",
                    st.session_state.balance.get("vendingMachine", {}).get("upgradeGoldCosts", []),
                )
            if section == "MergeGrid":
                merge_unlock_df = render_cost_list_table(
                    "블록별 해금 골드 비용", "merge_unlock_costs", "Block",
                    st.session_state.balance.get("mergeGrid", {}).get("unlockGoldCosts", []),
                )
            if section == "CharacterSlots":
                starting = st.session_state.balance.get("characterSlots", {}).get("startingSlots", 2)
                char_slot_costs_df = render_cost_list_table(
                    "슬롯 번호별 해금 골드 비용 (StartingSlots 다음 번호부터)", "char_slot_costs", "Slot",
                    st.session_state.balance.get("characterSlots", {}).get("unlockGoldCosts", []),
                    id_offset=starting + 1,
                )

    submitted = st.form_submit_button("시뮬레이션 실행", width="stretch")


if submitted:
    balance: dict = {}
    for f in SCALAR_FIELDS:
        section_key, field_key = _camel(f.section), _camel(f.field)
        balance.setdefault(section_key, {})[field_key] = edited_scalars[f.key]

    balance.setdefault("weapons", {})["table"] = weapons_df_to_table(weapons_df)
    balance.setdefault("weapons", {})["levelMultipliers"] = float_list_df_to_list(level_multipliers_df, "Level", "Multiplier")
    balance.setdefault("armor", {})["rollRanges"] = armor_ranges_df_to_dict(armor_ranges_df)
    balance.setdefault("currency", {})["armorMarketValues"] = armor_values_df_to_dict(armor_values_df)
    balance.setdefault("vendingMachine", {})["upgradeGoldCosts"] = cost_list_df_to_list(vm_upgrade_df, "Level")
    balance.setdefault("mergeGrid", {})["unlockGoldCosts"] = cost_list_df_to_list(merge_unlock_df, "Block")
    balance.setdefault("characterSlots", {})["unlockGoldCosts"] = cost_list_df_to_list(char_slot_costs_df, "Slot")

    st.session_state.balance = balance
    SCRATCH_BALANCE_JSON.write_text(json.dumps(balance, ensure_ascii=False, indent=2), encoding="utf-8")

    cmd = [
        "dotnet", "run", "--project", str(SIMULATOR_PROJECT), "-c", "Release", "--",
        str(runs_per_bot), "--balance", str(SCRATCH_BALANCE_JSON),
        "--summary", str(SCRATCH_SUMMARY_JSON), "--skip-llm",
    ]

    with st.spinner(f"시뮬레이터 실행 중... (봇 3종 x {runs_per_bot}회)"):
        start = time.perf_counter()
        proc = subprocess.run(cmd, cwd=BACKEND_ROOT, capture_output=True, text=True, encoding="utf-8", timeout=300)
        elapsed = time.perf_counter() - start

    if proc.returncode != 0:
        st.session_state.last_error = proc.stderr or proc.stdout
        st.session_state.summary = None
    else:
        st.session_state.last_error = None
        st.session_state.summary = json.loads(SCRATCH_SUMMARY_JSON.read_text(encoding="utf-8"))
        st.session_state.last_run_info = {"runs_per_bot": runs_per_bot, "elapsed": elapsed}


# ---------------------------------------------------------------------------
# 결과 표시
# ---------------------------------------------------------------------------

if st.session_state.last_error:
    st.error("시뮬레이터 실행 실패:\n\n" + st.session_state.last_error)

summary = st.session_state.summary
if summary is None:
    st.info("아직 실행 결과가 없습니다. 위에서 값을 조정하고 '시뮬레이션 실행'을 눌러주세요.")
else:
    info = st.session_state.last_run_info
    st.success(f"완료 — 봇 3종 x {info['runs_per_bot']}회, {info['elapsed']:.1f}초")

    bots_df = pd.DataFrame(summary["bots"])
    bots_df["botName"] = pd.Categorical(bots_df["botName"], categories=BOT_ORDER, ordered=True)
    bots_df = bots_df.sort_values("botName")

    col1, col2 = st.columns(2)
    with col1:
        fig = px.bar(bots_df, x="botName", y="winRate", title=f"봇별 승률 ({summary['maxStage']}스테이지 완주)", text_auto=".1%")
        fig.update_yaxes(tickformat=".0%")
        st.plotly_chart(fig, width="stretch")
    with col2:
        fig = px.bar(bots_df, x="botName", y="averageStagesCleared", title="평균 도달 스테이지", text_auto=".1f")
        st.plotly_chart(fig, width="stretch")

    weapon_rows = []
    for bot in summary["bots"]:
        for item in bot["weaponAdoption"]:
            weapon_rows.append({"botName": bot["botName"], "weaponType": item["weaponType"], "tier": f"T{item['tier']}", "adoptionRate": item["adoptionRate"]})
    if weapon_rows:
        weapon_df = pd.DataFrame(weapon_rows)
        fig = px.bar(
            weapon_df, x="weaponType", y="adoptionRate", color="tier", barmode="group",
            facet_col="botName", category_orders={"botName": BOT_ORDER, "weaponType": WEAPON_TYPES},
            title="무기 티어 채택률 (런 종료 시점 장착 스냅샷)",
        )
        fig.update_yaxes(tickformat=".0%")
        st.plotly_chart(fig, width="stretch")

    armor_rows = []
    for bot in summary["bots"]:
        for item in bot["armorAdoption"]:
            armor_rows.append({"botName": bot["botName"], "armorType": item["armorType"], "rarity": item["rarity"], "adoptionRate": item["adoptionRate"]})
    if armor_rows:
        armor_df = pd.DataFrame(armor_rows)
        fig = px.bar(
            armor_df, x="armorType", y="adoptionRate", color="rarity", barmode="group",
            facet_col="botName", category_orders={"botName": BOT_ORDER, "rarity": RARITY_ORDER},
            title="방어구 등급 채택률 (런 종료 시점 장착 스냅샷)",
        )
        fig.update_yaxes(tickformat=".0%")
        st.plotly_chart(fig, width="stretch")

    with st.expander("원본 summary.json"):
        st.json(summary)
