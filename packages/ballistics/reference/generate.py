"""Generate the reference dope tables the TypeScript solver is validated against.

The reference solver is py-ballisticcalc (https://github.com/o-murphy/py-ballisticcalc), an
independent, published open-source point-mass solver. Re-run this only when adding a scenario:

    python3 -m venv .venv && .venv/bin/pip install py-ballisticcalc==3.1.0
    .venv/bin/python packages/ballistics/reference/generate.py

It writes packages/ballistics/reference/reference-tables.json. The tests never import Python;
they only read that JSON file.
"""

import json
import pathlib

import py_ballisticcalc
from py_ballisticcalc import (
    Ammo,
    Atmo,
    Calculator,
    DragModel,
    PreferredUnits,
    Shot,
    TableG1,
    TableG7,
    Unit,
    Weapon,
    Wind,
)

PreferredUnits.distance = Unit.Meter
PreferredUnits.velocity = Unit.MPS
PreferredUnits.sight_height = Unit.Millimeter
PreferredUnits.temperature = Unit.Celsius
PreferredUnits.pressure = Unit.hPa
PreferredUnits.angular = Unit.Degree

GRAIN_KG = 6.479891e-5
INCH_M = 0.0254

# Reference loads (published bullet data).
LOADS = {
    # M118LR / 175 gr Sierra MatchKing, Litz-measured G7 BC.
    "m118lr": dict(table="G7", bc=0.243, grains=175, diameter_in=0.308, length_in=1.240, mv=792.5, twist_in=11.25),
    # .338 Lapua Magnum / 250 gr Lapua Scenar.
    "338lm": dict(table="G7", bc=0.322, grains=250, diameter_in=0.338, length_in=1.555, mv=900.0, twist_in=10.0),
    # .308 175 gr with the G1 BC, to exercise the G1 table.
    "308g1": dict(table="G1", bc=0.505, grains=175, diameter_in=0.308, length_in=1.240, mv=792.5, twist_in=11.25),
}

SIGHT_HEIGHT_MM = 45.0
ZERO_M = 100.0
WIND_MPS = 4.4704  # 10 mph full-value crosswind from the shooter's left


def atmo(kind):
    if kind == "icao":
        return Atmo.icao()
    if kind == "hot_high":
        return Atmo(altitude=1200, pressure=877.0, temperature=35, humidity=20)
    raise ValueError(kind)


def scenario(name, load, atmosphere, max_range, *, spin=False, coriolis=None):
    l = LOADS[load]
    table = TableG7 if l["table"] == "G7" else TableG1
    dm = DragModel(l["bc"], table, l["grains"], l["diameter_in"], Unit.Inch(l["length_in"]))
    weapon = Weapon(sight_height=SIGHT_HEIGHT_MM, twist=Unit.Inch(l["twist_in"]) if spin else 0)
    shot_kwargs = {}
    if coriolis:
        shot_kwargs = dict(latitude=coriolis["latitude"], azimuth=coriolis["azimuth"])
    # Zero with no wind (and without Coriolis, so Coriolis tables share the plain table's zero),
    # then fire with the crosswind.
    zero_shot = Shot(weapon=weapon, ammo=Ammo(dm, l["mv"]), atmo=atmo(atmosphere))
    calc = Calculator()
    zero_elev = calc.set_weapon_zero(zero_shot, ZERO_M)
    shot = Shot(
        weapon=weapon,
        ammo=Ammo(dm, l["mv"]),
        atmo=atmo(atmosphere),
        winds=[Wind(velocity=WIND_MPS, direction_from=90)],
        **shot_kwargs,
    )
    result = calc.fire(shot, trajectory_range=max_range, trajectory_step=100)
    rows = []
    for p in result:
        d = p.distance >> Unit.Meter
        if d < 50:
            continue
        rows.append(
            dict(
                range=round(d, 6),
                time=p.time,
                velocity=p.velocity >> Unit.MPS,
                height=p.height >> Unit.Meter,
                windage=p.windage >> Unit.Meter,
            )
        )
    return dict(
        name=name,
        load=load,
        atmosphere=atmosphere,
        spinDrift=spin,
        coriolis=coriolis,
        zeroElevationMrad=zero_elev >> Unit.MRad,
        rows=rows,
    )


def main():
    out = dict(
        generator=f"py-ballisticcalc {py_ballisticcalc.__version__ if hasattr(py_ballisticcalc, '__version__') else '3.1.0'}",
        sightHeightMm=SIGHT_HEIGHT_MM,
        zeroRangeM=ZERO_M,
        crosswindMps=WIND_MPS,
        loads=LOADS,
        atmospheres=dict(
            icao=dict(altitudeM=0, pressureHPa=1013.25, temperatureC=15, humidity=0),
            hot_high=dict(altitudeM=1200, pressureHPa=877.0, temperatureC=35, humidity=0.2),
        ),
        scenarios=[
            scenario("M118LR, ICAO, 10 mph crosswind", "m118lr", "icao", 1000),
            scenario(".338 LM, ICAO, 10 mph crosswind", "338lm", "icao", 1200),
            scenario(".308 G1, ICAO, 10 mph crosswind", "308g1", "icao", 1000),
            scenario("M118LR, hot and high (Dry Valley-like)", "m118lr", "hot_high", 1000),
            scenario(".338 LM, hot and high", "338lm", "hot_high", 1200),
            scenario("M118LR with spin drift", "m118lr", "icao", 1000, spin=True),
            scenario(".338 LM with spin drift", "338lm", "icao", 1200, spin=True),
            scenario(
                ".338 LM with Coriolis, 45N firing east",
                "338lm",
                "icao",
                1200,
                coriolis=dict(latitude=45.0, azimuth=90.0),
            ),
            scenario(
                ".338 LM with Coriolis, 45N firing north",
                "338lm",
                "icao",
                1200,
                coriolis=dict(latitude=45.0, azimuth=0.0),
            ),
        ],
    )
    path = pathlib.Path(__file__).with_name("reference-tables.json")
    path.write_text(json.dumps(out, indent=2) + "\n")
    print(f"wrote {path}")


if __name__ == "__main__":
    main()
