using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IniAirbus;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

using S = IniAirbusStateEvaluator;

/// <summary>
/// The iniBuilds A330-200 / A321LR First Officer (FirstOfficer/IniAirbus): every key it reads or writes
/// is one its definition registers (a key nobody registers reads NaN forever, and a write to it is a
/// silent dead write); every write is a control the definition's panel path can actually send; every
/// value in the key table is the generated map's MEASURED position for that label; the read-backs are
/// action-free; flows and checklists link up; and every engine, fuel, IRS and battery cut sits behind
/// its guard.
/// </summary>
public class IniAirbusFoTests
{
    /// <summary>The airframes whose generated map is in this build (the map-pinning tests).</summary>
    public static IEnumerable<object[]> Airframes() => new[] { new object[] { "A330" }, new object[] { "A321" } };

    /// <summary>Every airframe (the structure and guard tests, which need no map).</summary>
    public static IEnumerable<object[]> AllAirframes() => Airframes();

    internal static (IniA330Definition Def, IniAirbusKeys Keys) Of(string airframe) => airframe switch
    {
        "A330" => (new IniA330Definition(), IniAirbusKeys.A330),
        "A321" => (new IniA321Definition(), IniAirbusKeys.A321),
        _ => throw new ArgumentOutOfRangeException(nameof(airframe), airframe, null),
    };

    private static readonly HashSet<string> Synthetic = typeof(S)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .Where(v => v.StartsWith("FO_", StringComparison.Ordinal))
        .ToHashSet();

    private static bool IsPseudoKey(string key) =>
        key == IniAirbusActionExecutor.LandingLightsKey || key.StartsWith(IniAirbusActionExecutor.EcamPagePrefix, StringComparison.Ordinal);

    // ---------------------------------------------------------------- registration

    [Theory]
    [MemberData(nameof(Airframes))]
    public void Every_key_the_first_officer_reads_is_registered_or_one_of_its_own_synthetic_fields(string airframe)
    {
        var (def, keys) = Of(airframe);
        var vars = def.GetVariables();
        var reads = new List<string>();
        reads.AddRange(new S(keys).OnRequestPollFields);
        foreach (var g in IniAirbusChecklistDefinitions.Build(keys))
            foreach (var i in g.Items)
            {
                if (i.StateFieldName != null) reads.Add(i.StateFieldName);
                reads.AddRange(i.AdditionalStateFields);
            }
        foreach (var f in IniAirbusFlowDefinitions.Build(keys))
            foreach (var s in f.Steps)
                if (s.ConditionFieldName != null) reads.Add(s.ConditionFieldName);
        // the keys the synthetic fields and live values are built from
        reads.AddRange(keys.WriteKeys().Where(k => !IsPseudoKey(k)));
        reads.AddRange(keys.IrsModeReads);
        reads.Add(keys.ApuAvailLamp);
        reads.Add(keys.EcamPageRead);
        if (keys.ExtPowerAvailLamp != null) reads.Add(keys.ExtPowerAvailLamp);
        if (keys.CaptBaroUnit != null) reads.Add(keys.CaptBaroUnit);
        reads.AddRange(new[]
        {
            IniAirbusKeys.Flaps, IniAirbusKeys.SpoilersArmed, IniAirbusKeys.GearLever, IniAirbusKeys.GearNose,
            IniAirbusKeys.GearLeft, IniAirbusKeys.GearRight, IniAirbusKeys.ParkingBrake, IniAirbusKeys.Autobrake,
            IniAirbusKeys.V1, IniAirbusKeys.VR, IniAirbusKeys.V2, IniAirbusKeys.FuelTotalLb, IniAirbusKeys.WeightMetric,
            IniAirbusKeys.BaroMb, IniAirbusKeys.BaroMode, IniAirbusKeys.Eng1Avail, IniAirbusKeys.Eng2Avail, IniAirbusKeys.ApuFlap,
        });

        var missing = reads.Distinct().Where(k => !Synthetic.Contains(k) && !vars.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, $"{airframe}: unregistered First Officer read(s): {string.Join(", ", missing)}");
        var unknownSynthetic = reads.Distinct().Where(k => k.StartsWith("FO_") && !Synthetic.Contains(k)).ToList();
        Assert.True(unknownSynthetic.Count == 0, $"{airframe}: FO_ field(s) the evaluator does not compose: {string.Join(", ", unknownSynthetic)}");
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void Every_write_is_a_registered_control_the_definition_can_send(string airframe)
    {
        var (def, keys) = Of(airframe);
        var vars = def.GetVariables();
        foreach (var key in keys.WriteKeys())
        {
            Assert.True(vars.ContainsKey(key), $"{airframe}: write key {key} is not registered");
            Assert.True(def.FoWriteRoute(key) != null, $"{airframe}: {key} has no write route (not a writable control)");
        }
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Every_flow_and_checklist_write_is_a_first_officer_write_key(string airframe)
    {
        var (_, keys) = Of(airframe);
        var allowed = keys.WriteKeys().ToHashSet();
        foreach (var f in IniAirbusFlowDefinitions.Build(keys))
            foreach (var s in f.Steps)
            {
                var writes = s.ActionType == FlowStepActionType.SetSwitchMultiple ? s.MultiActions.Select(a => a.EventName)
                    : s.ActionType == FlowStepActionType.SetSwitch ? new[] { s.EventName! } : Array.Empty<string>();
                foreach (var w in writes)
                    Assert.True(allowed.Contains(w) || IsPseudoKey(w), $"{airframe} {f.Id}.{s.Id} writes {w}");
            }
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void The_ECAM_page_and_landing_light_pseudo_keys_resolve_to_registered_controls(string airframe)
    {
        var (def, keys) = Of(airframe);
        foreach (var page in IniAirbusKeys.EcamPageNumber.Keys)
            Assert.NotNull(def.FoWriteRoute(keys.EcamKey(page)));
        foreach (var cmd in new[] { IniAirbusActionExecutor.LandingOn, IniAirbusActionExecutor.LandingOff, IniAirbusActionExecutor.LandingStowed })
            foreach (var (key, _) in IniAirbusActionExecutor.LandingLightsPlan(keys, cmd))
                Assert.NotNull(def.FoWriteRoute(key));
    }

    // ---------------------------------------------------------------- the never-written controls

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void The_first_officer_never_writes_an_unprobed_or_stock_event_control(string airframe)
    {
        // ADIRS (never probed on the A330; the A340's are written — its code is readable), fire handles and
        // agents, IDG, RAT (never probed, generate_a330_map.py), the gear, parking brake, flaps and spoilers
        // (stock events / unverified). The engine masters and mode selector are the First Officer's since
        // 2026-10-28 (the engine start), and covered by the engine-start tests.
        var (_, keys) = Of(airframe);
        var forbidden = new List<string> { "_IR1", "_IR2", "_IR3", "_ADR", "_FIRE", "_AGENT", "_IDG", "_RAT", "_GEAR", "_PARKBRK",
            "_FLAPS", "_SPLR", "_SPDBRK", "_MAN_START", "_FADEC" };
        if (!keys.IrsWritable) forbidden.AddRange(new[] { "_IRS1_MODE", "_IRS2_MODE", "_IRS3_MODE" });
        foreach (var key in keys.WriteKeys())
        {
            Assert.False(forbidden.Any(f => key.Contains(f, StringComparison.Ordinal)), $"{airframe}: writes {key}");
        }
        Assert.DoesNotContain(IniAirbusKeys.Flaps, keys.WriteKeys());
        Assert.DoesNotContain(IniAirbusKeys.SpoilersArmed, keys.WriteKeys());
        Assert.DoesNotContain(IniAirbusKeys.ParkingBrake, keys.WriteKeys());
        Assert.DoesNotContain(IniAirbusKeys.GearLever, keys.WriteKeys());
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Fire_tests_are_the_captains_on_both_airframes(string airframe)
    {
        var (_, keys) = Of(airframe);
        Assert.Null(keys.EngFireTest);
        Assert.Null(keys.ApuFireTest);
        var steps = IniAirbusFlowDefinitions.Build(keys).Single(f => f.Id == "PREFLIGHT").Steps;
        Assert.Equal(FlowStepActionType.CaptainReminder, steps.Single(s => s.Id == "PF_FIRE_APU").ActionType);
        Assert.Equal(FlowStepActionType.CaptainReminder, steps.Single(s => s.Id == "PF_FIRE_ENG").ActionType);
        // the A330 keeps its T.O CONFIG key (probed ECAM key), the A340 its readable one; the A321 map has none
        Assert.Equal(airframe switch { "A330" => "A330_TO_CONFIG", "A340" => "A340_ECAM_TOCONFIG", _ => null }, keys.TakeoffConfigTest);
    }

    [Theory]
    [InlineData(double.NaN, double.NaN)]   // unread: never pressed
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]                 // already reads pressed (the APU running, or a stale 1): the Captain's
    public void The_A321_presses_APU_START_only_while_its_flag_reads_0(double flag, double expected)
    {
        var s = new S(IniAirbusKeys.A321);
        s.SetSimConnect(double.IsNaN(flag) ? SeededSimConnectCache.With()
            : SeededSimConnectCache.With((IniA330Definition.FoApuStartPbKey, flag)));
        Assert.Equal(expected, s.GetValue(S.ApuStartPressable));
        var a330 = new S(IniAirbusKeys.A330);
        Assert.Equal(1.0, a330.GetValue(S.ApuStartPressable));   // its START is its input event's own press

        foreach (var flowId in new[] { "BEFORE_START", "AFTER_LANDING" })
        {
            var steps = IniAirbusFlowDefinitions.Build(IniAirbusKeys.A321).Single(f => f.Id == flowId).Steps;
            var start = steps.Single(x => x.Id.EndsWith("_APU_START"));
            var captain = steps.Single(x => x.Id.EndsWith("_APU_START_CAPT"));
            Assert.True(steps.IndexOf(captain) < steps.IndexOf(start));
            Assert.Equal(!(expected > 0.5), start.LeaveAloneWhen!(s));   // left alone exactly when not pressable (unread included)
            Assert.Equal(expected > 0.5, captain.SkipCondition!(s));     // the Captain asked exactly then
        }
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    [InlineData(2.0, true)]
    [InlineData(3.0, false)]
    public void The_A321_autobrake_max_is_pressed_only_from_off(double level, bool captains)
    {
        var s = new S(IniAirbusKeys.A321);
        s.SetSimConnect(SeededSimConnectCache.With((IniAirbusKeys.Autobrake, level)));
        Assert.Equal(captains, s.AutobrakeMaxLeftToCaptain());
        var a330 = new S(IniAirbusKeys.A330);
        a330.SetSimConnect(SeededSimConnectCache.With((IniAirbusKeys.Autobrake, level)));
        Assert.False(a330.AutobrakeMaxLeftToCaptain());   // the A330's HI press arms MAX from any level
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Securing_ticks_carry_the_on_ground_guard(string airframe)
    {
        var items = IniAirbusChecklistDefinitions.Build(Of(airframe).Keys).Single(g => g.Id == "SECURE").Items;
        var unread = new S(Of(airframe).Keys);
        foreach (var id in new[] { "SC_OXY", "SC_APUMASTER", "SC_EMEREXIT", "SC_BATS" })
            Assert.True(items.Single(i => i.Id == id).LeaveAloneWhen!(unread), id);
        var epu = IniAirbusChecklistDefinitions.Build(Of(airframe).Keys).Single(g => g.Id == "ELEC_POWER_UP")
            .Items.Single(i => i.Id == "EPU_EXTPWR");
        var dark = new S(Of(airframe).Keys);
        dark.SetSimConnect(SeededSimConnectCache.With((Of(airframe).Keys.ExtPowerAvailLamp!, 0)));
        Assert.True(epu.LeaveAloneWhen!(dark));
    }

    [Fact]
    public void The_A321_has_no_fire_test_config_test_or_cabin_call_write()
    {
        var k = IniAirbusKeys.A321;
        Assert.Null(k.EngFireTest);
        Assert.Null(k.ApuFireTest);
        Assert.Null(k.TakeoffConfigTest);
        Assert.Null(k.CabinCallAll);
        // and never the A330 standby pumps the A321 does not have
        Assert.DoesNotContain(k.FuelPumps, p => p.Key.Contains("STBY"));
        Assert.DoesNotContain("A321_APU_BAT", k.Batteries);
    }

    // ---------------------------------------------------------------- polarity vs the measured map

    public static IEnumerable<object[]> Positions()
    {
        foreach (var af in new[] { "A330", "A321" })
        {
            var k = af == "A330" ? IniAirbusKeys.A330 : IniAirbusKeys.A321;
            foreach (var b in k.Batteries) { yield return R(af, b, "Auto", 1); yield return R(af, b, "Off", 0); }
            foreach (var e in k.ExtPower) { yield return R(af, e, "On", 1); yield return R(af, e, "Off", 0); }
            yield return R(af, k.ApuMaster, "On", 1); yield return R(af, k.ApuMaster, "Off", 0);
            yield return R(af, k.ApuBleed, "On", 1); yield return R(af, k.ApuBleed, "Off", 0);
            foreach (var (p, on, off) in k.FuelPumps) { yield return R(af, p, "On", on); yield return R(af, p, "Off", off); }
            foreach (var p in k.Packs) yield return R(af, p, "On", 1);
            yield return R(af, k.CrossBleed, "Auto", 1);
            yield return R(af, k.PackFlow, "Normal", 1);
            foreach (var h in k.HotAir) yield return R(af, h, "On", 1);
            yield return R(af, k.PressMode!, "Auto", k.PressModeAuto);
            foreach (var i in k.IrsModeReads) { yield return R(af, i, "Nav", 1); yield return R(af, i, "Off", 0); }
            yield return R(af, k.CrewOxygen, "On", 1); yield return R(af, k.CrewOxygen, "Off", 0);
            yield return R(af, k.Strobe, "On", 0); yield return R(af, k.Strobe, "Auto", 1); yield return R(af, k.Strobe, "Off", 2);
            yield return R(af, k.Beacon, "On", 1); yield return R(af, k.Beacon, "Off", 0);
            yield return R(af, k.WingLights, "On", 1); yield return R(af, k.WingLights, "Off", 0);
            yield return R(af, k.NavLogo, "1", 1); yield return R(af, k.NavLogo, "Off", 2);
            yield return R(af, k.RunwayTurnoff, "On", 1); yield return R(af, k.RunwayTurnoff, "Off", 0);
            yield return R(af, k.NoseLight, "Take-off", 0); yield return R(af, k.NoseLight, "Taxi", 1); yield return R(af, k.NoseLight, "Off", 2);
            foreach (var (key, on, off, stowed) in k.LandingLights)
            {
                yield return R(af, key, "On", on); yield return R(af, key, "Off", off);
                if (stowed != off) yield return R(af, key, "Retract", stowed);
            }
            yield return R(af, k.SeatBelts, "On", k.SeatBeltsOn); yield return R(af, k.SeatBelts, "Off", k.SeatBeltsOff);
            yield return R(af, k.NoSmoking, "On", 0); yield return R(af, k.NoSmoking, "Auto", 1); yield return R(af, k.NoSmoking, "Off", 2);
            yield return R(af, k.EmerExit, "Armed", 1); yield return R(af, k.EmerExit, "Off", 2);
            yield return R(af, k.Dome!, "Bright", 0); yield return R(af, k.Dome!, "Dim", 1); yield return R(af, k.Dome!, "Off", 2);
            yield return R(af, k.AnnLights, "Bright", 1); yield return R(af, k.AnnLights, "Dim", 2);
            yield return R(af, k.StbyCompassLight, "On", 1); yield return R(af, k.StbyCompassLight, "Off", 0);
            foreach (var w in k.Wipers) yield return R(af, w, "Off", 0);
            foreach (var a in k.EngAntiIce.Append(k.WingAntiIce)) { yield return R(af, a, "On", 1); yield return R(af, a, "Off", 0); }
            // the engine masters and mode selector (2026-10-28, NOT verified in the sim on these two airframes)
            foreach (var m in k.EngineMasters) { yield return R(af, m, "On", 1); yield return R(af, m, "Off", 0); }
            yield return R(af, k.EngineModeSelector, "Normal", IniAirbusKeys.ModeNorm);
            yield return R(af, k.EngineModeSelector, "Ignition start", IniAirbusKeys.ModeIgnStart);
            foreach (var x in new[] { k.CaptFd, k.FoFd, k.CaptLs, k.FoLs }) { yield return R(af, x, "On", 1); yield return R(af, x, "Off", 0); }
            yield return R(af, k.XpdrMode, "Standby", 0); yield return R(af, k.XpdrMode, "Auto", 1);
            yield return R(af, k.TcasMode, "Standby", 0); yield return R(af, k.TcasMode, "TA/RA", 2);
            yield return R(af, k.TcasTraffic, "All", 1);
            if (k.AltReporting != null) yield return R(af, k.AltReporting, "On", 1);
            yield return R(af, k.WxSystem, "1", 0); yield return R(af, k.WxSystem, "Off", 1);
            yield return R(af, k.Pws, "Off", 0); yield return R(af, k.Pws, "Auto", 1);
        }
        static object[] R(string af, string key, string label, int value) => new object[] { af, key, label, value };
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void Every_value_the_first_officer_uses_is_the_maps_measured_position(string airframe, string key, string label, int value)
    {
        var (def, _) = Of(airframe);
        Assert.Equal((double)value, def.FoPositionValue(key, label));
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void The_apu_and_external_power_lamps_are_the_available_lights(string airframe)
    {
        var (def, keys) = Of(airframe);
        var vars = def.GetVariables();
        Assert.Equal("INI_APU_AVAILABLE", vars[keys.ApuAvailLamp].Name);
        Assert.Equal(airframe == "A330" ? "INI_GPU_AVAIL" : "INI_EXT_PWR_AVAILABLE_AC", vars[keys.ExtPowerAvailLamp!].Name);
        Assert.Equal("INI_ECAM_ACTIVE_PAGE", vars[keys.EcamPageRead].Name);
        Assert.Equal("ENGINE_1_AVAIL", vars[IniAirbusKeys.Eng1Avail].Name);
        Assert.Equal("ENGINE_2_AVAIL", vars[IniAirbusKeys.Eng2Avail].Name);
        Assert.Equal("INI_APU_FLAP_PERCENT", vars[IniAirbusKeys.ApuFlap].Name);
        // the FO's own reads are OnRequest (polled by the window), never on the continuous batch
        foreach (var k in new[] { IniAirbusKeys.Eng1Avail, IniAirbusKeys.Eng2Avail, IniAirbusKeys.ApuFlap, IniAirbusKeys.GearLeft, IniAirbusKeys.GearRight })
        {
            Assert.Equal(UpdateFrequency.OnRequest, vars[k].UpdateFrequency);
            Assert.False(vars[k].IsAnnounced);
        }
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void The_A321_landing_lights_are_one_switch_per_side_and_the_A330s_one_switch(string airframe)
    {
        var (_, keys) = Of(airframe);
        var on = IniAirbusActionExecutor.LandingLightsPlan(keys, IniAirbusActionExecutor.LandingOn);
        var stow = IniAirbusActionExecutor.LandingLightsPlan(keys, IniAirbusActionExecutor.LandingStowed);
        if (airframe == "A330")
        {
            Assert.Equal(new[] { ("A330_LANDING_LT", 1) }, on);
            Assert.Equal(new[] { ("A330_LANDING_LT", 0) }, stow);
        }
        else if (airframe == "A340")
        {
            Assert.Equal(new[] { ("A340_LDGLIGHT_TOGGLE", 1) }, on);
            Assert.Equal(new[] { ("A340_LDGLIGHT_TOGGLE", 0) }, stow);
        }
        else
        {
            // 0 = ON, 2 = RETRACT (measured: docs/ini-a321.md); the dead single INI_LANDING_LIGHT_SWITCH is never used
            Assert.Equal(new[] { ("A321_LDG_LT_L", 0), ("A321_LDG_LT_R", 0) }, on);
            Assert.Equal(new[] { ("A321_LDG_LT_L", 2), ("A321_LDG_LT_R", 2) }, stow);
        }
    }

    // ---------------------------------------------------------------- structure and links

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Read_back_groups_are_action_free(string airframe)
    {
        foreach (var g in IniAirbusChecklistDefinitions.Build(Of(airframe).Keys).Where(g => g.Id.EndsWith("_CL")))
            foreach (var i in g.Items)
                Assert.True(i.CheckAction == null, $"{airframe} {g.Id}.{i.Id} has an action (FO-3)");
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Item_and_step_ids_are_unique_and_every_link_resolves_inside_the_flows_completion_groups(string airframe)
    {
        var keys = Of(airframe).Keys;
        var groups = IniAirbusChecklistDefinitions.Build(keys);
        var itemIds = groups.SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(itemIds.Count, itemIds.Distinct().Count());
        foreach (var g in groups) Assert.All(g.Items, i => Assert.Equal(g.Id, i.GroupId));

        bool Exists(string id) => groups.Any(g => g.Id == id);
        foreach (var f in IniAirbusFlowDefinitions.Build(keys))
        {
            Assert.Equal(f.Steps.Count, f.Steps.Select(s => s.Id).Distinct().Count());
            var completion = f.CompletionGroupIds(Exists).ToHashSet();
            var linkable = groups.Where(g => completion.Contains(g.Id)).SelectMany(g => g.Items).Select(i => i.Id).ToHashSet();
            foreach (var s in f.Steps)
                foreach (var link in s.LinkedChecklistItemIds)
                    Assert.True(linkable.Contains(link), $"{airframe} {f.Id}.{s.Id} links {link}, outside its completion groups");
        }
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Every_auto_detected_action_line_in_a_flow_latched_group_has_a_delivering_step(string airframe)
    {
        // FO-7: a finished flow latches its group, so a readable line nothing delivers would be ticked over a
        // switch that never moved. Read-back (*_CL) lines that a later state confirms are excluded.
        var keys = Of(airframe).Keys;
        var groups = IniAirbusChecklistDefinitions.Build(keys);
        var flows = IniAirbusFlowDefinitions.Build(keys);
        var linked = flows.SelectMany(f => f.Steps).SelectMany(s => s.LinkedChecklistItemIds).ToHashSet();
        foreach (var f in flows)
            foreach (var gid in f.CompletionGroupIds(id => groups.Any(g => g.Id == id)).Where(id => !id.EndsWith("_CL")))
                foreach (var i in groups.Single(g => g.Id == gid).Items.Where(i => i.IsAutoDetectable || i.CheckAction != null))
                    Assert.True(linked.Contains(i.Id), $"{airframe} {gid}.{i.Id} has no flow step behind it");
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Every_group_is_departure_or_arrival_and_related_flows_resolve(string airframe)
    {
        var keys = Of(airframe).Keys;
        var flows = IniAirbusFlowDefinitions.Build(keys);
        foreach (var g in IniAirbusChecklistDefinitions.Build(keys))
        {
            Assert.True(FoDepartureGroups.Departure.Contains(g.Id) ^ FoDepartureGroups.Arrival.Contains(g.Id), g.Id);
            if (!g.Id.EndsWith("_CL")) Assert.NotNull(RelatedFlow.For(flows, g.Id));
        }
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Gear_checks_are_read_only_and_last(string airframe)
    {
        var flows = IniAirbusFlowDefinitions.Build(Of(airframe).Keys);
        var up = flows.Single(f => f.Id == "AFTER_TAKEOFF").Steps;
        Assert.Equal("AT_GEAR_UP_CHECK", up[^1].Id);
        Assert.Equal(FlowStepActionType.WaitForCondition, up[^1].ActionType);
        var down = flows.Single(f => f.Id == "LANDING").Steps;
        Assert.Equal("LD_GEAR_DOWN_CHECK", down[^1].Id);
        Assert.Equal(FlowStepActionType.WaitForCondition, down[^1].ActionType);
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void The_landing_autobrake_is_never_automated(string airframe)
    {
        // FO-14: only the Taxi flow's MAX (RTO) writes the autobrake
        var keys = Of(airframe).Keys;
        foreach (var f in IniAirbusFlowDefinitions.Build(keys))
            foreach (var s in f.Steps.Where(s => s.EventName == IniAirbusKeys.Autobrake))
                Assert.Equal(("TAXI", keys.AutobrakeMax), (f.Id, s.TargetValue ?? -1));
    }

    // ---------------------------------------------------------------- safety guards (FO-25)

    private static void AssertGuard(FlowStep<S> g, string field)
    {
        Assert.Equal(FlowStepActionType.WaitForCondition, g.ActionType);
        Assert.Equal(field, g.ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, g.FailurePolicy);
        Assert.False(g.Condition!(double.NaN));
        Assert.False(g.Condition!(0));
        Assert.Null(g.SkipCondition);
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Cold_and_dark_power_up_stops_unless_the_engines_read_stopped(string airframe)
    {
        var steps = IniAirbusFlowDefinitions.Build(Of(airframe).Keys).Single(f => f.Id == "ELECTRICAL_POWER_UP").Steps;
        AssertGuard(steps[0], S.EnginesOff);
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Shutdown_cuts_fuel_only_on_the_ground_and_after_both_engines_read_stopped(string airframe)
    {
        var steps = IniAirbusFlowDefinitions.Build(Of(airframe).Keys).Single(f => f.Id == "SHUTDOWN").Steps;
        AssertGuard(steps[0], "SIM_ON_GROUND");
        int stopped = steps.FindIndex(s => s.Id == "SD_ENGINES_OFF");
        int power = steps.FindIndex(s => s.Id == "SD_POWER_SOURCE");
        int park = steps.FindIndex(s => s.Id == "SD_PARKBRAKE");
        int pumps = steps.FindIndex(s => s.Id == "SD_FUELPUMPS_OFF");
        int beacon = steps.FindIndex(s => s.Id == "SD_BEACON_OFF");
        var keys = Of(airframe).Keys;
        for (int n = 1; n <= keys.EngineCount; n++)
        {
            // the First Officer cuts each master: after the brake check and the power source, before the
            // stopped wait, and only when the parking brake check completed
            int master = steps.FindIndex(s => s.Id == $"SD_ENG{n}_OFF");
            Assert.True(park < master && power < master && master < stopped, $"engine {n}");
            Assert.Equal(FlowStepActionType.SetSwitch, steps[master].ActionType);
            Assert.Equal((keys.Master(n), 0), (steps[master].EventName, steps[master].TargetValue ?? -1));
            Assert.Equal("SD_PARKBRAKE", steps[master].RequiresStepId);
        }
        Assert.DoesNotContain(steps, s => s.Id == "SD_ENGINES_CAPT");
        Assert.True(stopped < pumps && stopped < beacon);
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[stopped].FailurePolicy);
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[power].FailurePolicy);
        Assert.False(steps[stopped].Condition!(double.NaN));
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Securing_cuts_the_batteries_only_on_the_ground_with_the_engines_stopped(string airframe)
    {
        var steps = IniAirbusFlowDefinitions.Build(Of(airframe).Keys).Single(f => f.Id == "SECURE").Steps;
        AssertGuard(steps[0], "SIM_ON_GROUND");
        AssertGuard(steps[1], S.EnginesOff);
        int flap = steps.FindIndex(s => s.Id == "SC_APU_WAIT");
        int ext = steps.FindIndex(s => s.Id == "SC_EXTPWR_OFF");
        int bats = steps.FindIndex(s => s.Id == "SC_BATS");
        Assert.True(flap < ext && ext < bats);   // FOA-10 order
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void External_power_goes_off_before_start_only_after_the_APU_is_confirmed(string airframe)
    {
        var steps = IniAirbusFlowDefinitions.Build(Of(airframe).Keys).Single(f => f.Id == "BEFORE_START").Steps;
        int avail = steps.FindIndex(s => s.Id == "BS_APU_AVAIL"), ext = steps.FindIndex(s => s.Id == "BS_EXTPWR_OFF");
        Assert.True(avail < ext);
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[avail].FailurePolicy);
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Engine_start_is_the_first_officers_one_engine_at_a_time_and_stops_on_a_failure(string airframe)
    {
        var keys = Of(airframe).Keys;
        var steps = IniAirbusFlowDefinitions.Build(keys).Single(f => f.Id == "ENGINE_START").Steps;
        var expected = new List<string> { "ES_ON_GROUND", "ES_ECAMENG", "ES_MODE" };
        foreach (int n in keys.StartOrder) { expected.Add($"ES_ENG{n}"); expected.Add($"ES_ENG{n}_WAIT"); }
        expected.Add("ES_MODE_NORM");
        Assert.Equal(expected, steps.Select(s => s.Id));
        AssertGuard(steps[0], "SIM_ON_GROUND");

        // the selector: IGN START, read back, a failure STOPS the flow
        var mode = steps.Single(s => s.Id == "ES_MODE");
        Assert.Equal((keys.EngineModeSelector, IniAirbusKeys.ModeIgnStart), (mode.EventName, mode.TargetValue ?? -1));
        Assert.Equal(FlowStepFailurePolicy.Stop, mode.FailurePolicy);
        Assert.Equal(keys.EngineModeSelector, mode.VerifyFieldName);
        Assert.True(mode.VerifyCondition!(2));
        Assert.False(mode.VerifyCondition!(1));
        Assert.False(mode.VerifyCondition!(double.NaN));

        foreach (int n in keys.StartOrder)
        {
            // each master ON: read back, Stop, only after the selector, never pressed over a running engine
            var master = steps.Single(s => s.Id == $"ES_ENG{n}");
            Assert.Equal((keys.Master(n), 1), (master.EventName, master.TargetValue ?? -1));
            Assert.Equal(FlowStepFailurePolicy.Stop, master.FailurePolicy);
            Assert.Equal("ES_MODE", master.RequiresStepId);
            Assert.Equal(keys.Master(n), master.VerifyFieldName);
            Assert.False(master.VerifyCondition!(0));
            var running = new S(keys);
            running.SetSimConnect(SeededSimConnectCache.With((keys.EngineAvail[n - 1], 1.0), (keys.Master(n), 0.0)));
            Assert.True(master.SkipCondition!(running), $"engine {n} running: never pressed");
            var stopped = new S(keys);
            stopped.SetSimConnect(SeededSimConnectCache.With((keys.EngineAvail[n - 1], 0.0), (keys.Master(n), 0.0)));
            Assert.False(master.SkipCondition!(stopped), $"engine {n} stopped: pressed");

            // ...then that engine waited until it reads STARTED, Stop on a timeout
            var wait = steps.Single(s => s.Id == $"ES_ENG{n}_WAIT");
            Assert.Equal(S.StartedField(n), wait.ConditionFieldName);
            Assert.Equal(FlowStepFailurePolicy.Stop, wait.FailurePolicy);
            Assert.Equal(180, wait.TimeoutSeconds);
            Assert.False(wait.Condition!(double.NaN));
            Assert.Contains("master stays ON", wait.Label);
        }
        var norm = steps.Single(s => s.Id == "ES_MODE_NORM");
        Assert.Equal((keys.EngineModeSelector, IniAirbusKeys.ModeNorm), (norm.EventName, norm.TargetValue ?? -1));
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Checklist_ticks_never_cut_fuel_or_batteries_unguarded(string airframe)
    {
        var groups = IniAirbusChecklistDefinitions.Build(Of(airframe).Keys);
        var pumps = groups.Single(g => g.Id == "SHUTDOWN").Items.Single(i => i.Id == "SD_FUELPUMPS_OFF");
        var bats = groups.Single(g => g.Id == "SECURE").Items.Single(i => i.Id == "SC_BATS");
        var ext = groups.Single(g => g.Id == "BEFORE_START").Items.Single(i => i.Id == "BS_EXTPWR_OFF");
        var unread = new S(Of(airframe).Keys);   // nothing read: every guard must hold
        Assert.True(pumps.LeaveAloneWhen!(unread));
        Assert.True(bats.LeaveAloneWhen!(unread));
        Assert.True(ext.LeaveAloneWhen!(unread));
    }

    [Theory]
    [MemberData(nameof(AllAirframes))]
    public void Toggling_autobrake_max_is_never_pressed_over_an_unread_level(string airframe)
    {
        var step = IniAirbusFlowDefinitions.Build(Of(airframe).Keys).Single(f => f.Id == "TAXI").Steps.Single(s => s.Id == "TX_AUTOBRAKE");
        Assert.True(step.LeaveAloneWhen!(new S(Of(airframe).Keys)));
        Assert.True(IniAirbusChecklistDefinitions.Build(Of(airframe).Keys).Single(g => g.Id == "TAXI")
            .Items.Single(i => i.Id == "TX_AUTOBRAKE").LeaveAloneWhen!(new S(Of(airframe).Keys)));
    }
}

/// <summary>The evaluator's pure readings and the executor's refusal rule.</summary>
public class IniAirbusFoEvaluatorTests
{
    [Theory]
    [InlineData(1.0, double.NaN, 1.0)]        // the aircraft's own AVAIL wins
    [InlineData(1.0, 20.0, 1.0)]
    [InlineData(0.0, 70.0, 0.0)]               // AVAIL read 0: not available, whatever N2 says
    [InlineData(double.NaN, 48.5, 1.0)]        // AVAIL unread: N2 at the A330 panel state's idle
    [InlineData(double.NaN, 40.0, 0.0)]
    [InlineData(double.NaN, double.NaN, double.NaN)]
    public void Running_reads_the_engine_avail_then_n2(double avail, double n2, double expected)
        => Assert.Equal(expected, S.RunningFrom(avail, n2));

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0, 1.0)]
    [InlineData(0.0, 0.0, double.NaN, double.NaN, 1.0)]
    [InlineData(0.0, 30.0, 0.0, 0.0, 0.0)]
    [InlineData(0.0, 0.0, 0.0, 1.0, 0.0)]      // an AVAIL lit is never "stopped"
    [InlineData(0.0, double.NaN, 0.0, 0.0, double.NaN)]
    public void Engines_off_needs_both_n2_read_low(double n2a, double n2b, double av1, double av2, double expected)
        => Assert.Equal(expected, S.EnginesOffFrom(n2a, n2b, av1, av2));

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0, true, 1.0)]
    [InlineData(0.0, 0.0, 50.0, 0.0, true, 0.0)]
    [InlineData(1.0, 100.0, 100.0, 100.0, false, 1.0)]
    [InlineData(1.0, 100.0, 90.0, 100.0, false, 0.0)]
    [InlineData(0.0, 100.0, 100.0, 100.0, false, 0.0)]  // lever up: never "down"
    [InlineData(1.0, double.NaN, 100.0, 100.0, false, double.NaN)]
    public void Gear_needs_the_lever_and_every_leg(double lever, double nose, double left, double right, bool up, double expected)
        => Assert.Equal(expected, S.GearFrom(lever, nose, left, right, up));

    [Fact]
    public void Unread_state_never_reads_true()
    {
        var s = new S(IniAirbusKeys.A330);
        foreach (var f in new[] { S.EnginesOff, S.Eng1Running, S.FuelPumpsOn, S.FuelPumpsOff, S.BatteriesOff, S.ApuOrExtPower,
                     S.GearUp, S.GearDown, S.TaxiMemoOk, S.LdgMemoOk, S.IrsNav, S.IrsOff, S.ExtPowerOff, S.LandingLightsOn })
            Assert.True(double.IsNaN(s.GetValue(f)), f);
    }

    [Fact]
    public void Fuel_pumps_read_on_only_with_every_pump_on()
    {
        var k = IniAirbusKeys.A321;
        var s = new S(k);
        s.SetSimConnect(SeededSimConnectCache.With(k.FuelPumps.Select(p => (p.Key, 1.0)).ToArray()));
        Assert.Equal(1, s.GetValue(S.FuelPumpsOn));
        s.SetSimConnect(SeededSimConnectCache.With(k.FuelPumps.Select((p, i) => (p.Key, i == 3 ? 0.0 : 1.0)).ToArray()));
        Assert.Equal(0, s.GetValue(S.FuelPumpsOn));
        Assert.Equal(0, s.GetValue(S.FuelPumpsOff));
    }

    [Fact]
    public void The_A321_landing_lights_read_off_when_retracted()
    {
        var s = new S(IniAirbusKeys.A321);
        s.SetSimConnect(SeededSimConnectCache.With(("A321_LDG_LT_L", 2), ("A321_LDG_LT_R", 2)));
        Assert.Equal(1, s.GetValue(S.LandingLightsOff));
        Assert.Equal(0, s.GetValue(S.LandingLightsOn));
        s.SetSimConnect(SeededSimConnectCache.With(("A321_LDG_LT_L", 0), ("A321_LDG_LT_R", 0)));
        Assert.Equal(1, s.GetValue(S.LandingLightsOn));
    }

    [Fact]
    public void Baro_reads_standard_from_the_cockpit_mode()
    {
        var s = new S(IniAirbusKeys.A330);
        s.SetSimConnect(SeededSimConnectCache.With((IniAirbusKeys.BaroMode, 3), (IniAirbusKeys.BaroMb, 1013)));
        Assert.Equal("standard", s.BaroText());
        s.SetSimConnect(SeededSimConnectCache.With((IniAirbusKeys.BaroMode, 0), (IniAirbusKeys.BaroMb, 1009), ("A330_CPT_BARO_UNIT", 1)));
        Assert.Equal("QNH 1009", s.BaroText());
        s.SetSimConnect(SeededSimConnectCache.With((IniAirbusKeys.BaroMode, 0), (IniAirbusKeys.BaroMb, 1013.2), ("A330_CPT_BARO_UNIT", 0)));
        Assert.Equal("QNH 29.92", s.BaroText());
    }

    [Theory]
    [InlineData(false, true, false, true, false, false, "not a registered control")]
    [InlineData(true, false, false, true, false, false, "not a control the First Officer may write")]
    [InlineData(true, true, true, false, false, false, "the MobiFlight calculator path is not available")]
    [InlineData(true, true, false, true, false, false, null)]
    [InlineData(true, true, false, true, true, false, "a toggling key whose state is not read yet")]
    public void A_write_that_cannot_land_is_refused(bool registered, bool routed, bool calc, bool calcCanLand, bool toggle,
        bool _, string? expected)
    {
        (bool, string?, bool, int)? route = routed ? (calc || toggle, null, toggle, 0) : null;
        Assert.Equal(expected, IniAirbusActionExecutor.Refusal("K", registered, route, calcCanLand, _ => true, null));
    }

    [Fact]
    public void An_input_event_this_aircraft_lacks_is_refused()
    {
        Assert.NotNull(IniAirbusActionExecutor.Refusal("K", true, (false, "AIRLINER_PACK1", false, 0), true, _ => false, 1));
        Assert.Null(IniAirbusActionExecutor.Refusal("K", true, (false, "AIRLINER_PACK1", false, 0), false, _ => true, 1));
    }

    [Fact]
    public void The_A330s_config_test_is_a_held_momentary_key()
    {
        var def = new IniA330Definition();
        Assert.Equal(3000, def.FoWriteRoute("A330_TO_CONFIG")!.Value.HoldMs);
        Assert.True(def.FoWriteRoute("A330_PACK1")!.Value is { Calc: false, InputEvent: "AIRLINER_PACK1" });
        Assert.True(def.FoWriteRoute("A330_AUTOBRAKE")!.Value is { Calc: false, InputEvent: "AIRLINER_AUTOBRK_HI" });
        var a321 = new IniA321Definition();
        Assert.True(a321.FoWriteRoute("A330_AUTOBRAKE")!.Value.Calc);
        Assert.True(a321.FoWriteRoute("A321_PRESS_MODE")!.Value.Toggle);
        Assert.True(a321.FoWriteRoute("A321_PACK1")!.Value is { Calc: true, Toggle: false });
        Assert.Null(def.FoWriteRoute("A330_ECAM_PAGE"));   // a status row
        Assert.Null(def.FoWriteRoute("A330_FLAPS"));        // the definition's own stock-event control
    }
}
