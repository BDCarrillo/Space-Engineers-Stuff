
/*
        Threat Score script calculates the Modular Encounters Spawner threat level score for the grid.
        To preserve server performance it runs only once, if you change any blocks or grid connections, manually run again.
        The score and details per block type (power, weapons, etc) are shown in the programmable block control panel.
        Original Workshop link: https://steamcommunity.com/sharedfiles/filedetails/?id=2498906212
        It is an approximation because ingame scripts cannot get the ModId of a block, so they cannot reliably double the threat score of modded blocks.
        The script is only aware of a few modded blocks from Draconis Impossible Extended (https://wiki.sigmadraconis.games/doku.php?id=di:draconis_impossible).
        Finally, remember the total threat score is calculated for all grids in a given sphere radius (other players, unknown signals, etc).
        Modular Encounters Spawner mod: https://steamcommunity.com/workshop/filedetails/?id=1521905890
        Original work by StalkR, modified by BDCarrillo and Trekker as permitted by original license: https://github.com/StalkR/Space-Engineers-Stuff/blob/master/LICENSE
        */

// Calculate the threat score of the current grid and connected grids (true), or only the current grid (false).
bool multiGrid = true;

// If you get "error: Script execution terminated, script is too complex" it's because the grid is too large for
// counting all the non-terminal blocks, because unfortunately the game doesn't expose them (see below).
// In that case:
//  - switch to `multiGrid = false` above
//  - open the 'info' tab for the grid and write the total number of blocks below
int totalBlocks = 0;

WcPbApi wC = new WcPbApi();

HashSet<MyDefinitionId> wCFixed = new HashSet<MyDefinitionId>();
HashSet<MyDefinitionId> wCTurret = new HashSet<MyDefinitionId>();

public Program()
{
    if (wC.Activate(Me))
    {
        wC.GetAllCoreStaticLaunchers(wCFixed);
        wC.GetAllCoreTurrets(wCTurret);
    }
    Runtime.UpdateFrequency = UpdateFrequency.Once;
}

private List<string> moddedBlocks = new List<string>() {
    // Tracking Beacon https://steamcommunity.com/sharedfiles/filedetails/?id=2222198080
    "MyObjectBuilder_Beacon/DetectionLargeBlockBeacon",
    "MyObjectBuilder_Beacon/DetectionSmallBlockBeacon",
    // Condensor https://steamcommunity.com/sharedfiles/filedetails/?id=2362559900
    "Refinery/Condensor",
    "Refinery/LargeCondensor",
    // Daily Needs https://steamcommunity.com/sharedfiles/filedetails/?id=1608841667
    "Refinery/WRS",
    "Refinery/WRSSmall",
    "Assembler/CropGrower",
    "Refinery/Hydroponics",
    "Refinery/HydroponicsSmall",
    "Refinery/Hydroponics2",
    "Assembler/Kitchen",
    "Assembler/KitchenSmall",
    "Assembler/EmergencyRationsKitSmall",
    "Refinery/LargeBiomassEngine",
    "Refinery/SmallBiomassEngine"
};



private bool IsMod(IMyTerminalBlock block)
{
    var key = block.BlockDefinition.TypeId.ToString() + "/" + block.BlockDefinition.SubtypeId.ToString();
    return moddedBlocks.Contains(key);
}


List<IMyTerminalBlock> list = new List<IMyTerminalBlock>();
private float BlocksThreat<T>(Func<IMyTerminalBlock, float> score, Func<IMyTerminalBlock, Boolean> collect = null) where T : class
{
    list.Clear();
    GridTerminalSystem.GetBlocksOfType<T>(list,
        b => b.CubeGrid.EntityId == Me.CubeGrid.EntityId && b.IsFunctional && (collect == null || collect(b)));
    return list.Sum(b => score(b));
}

private Dictionary<long, float> BlocksThreatPerGrid<T>(
        Func<IMyTerminalBlock, float> score,
        Func<IMyTerminalBlock, Boolean> collect = null
    ) where T : class
{
    list.Clear();
    GridTerminalSystem.GetBlocksOfType<T>(list, b => b.IsFunctional && (collect == null || collect(b)));
    var m = new Dictionary<long, float>();
    foreach (var b in list)
    {
        var k = b.CubeGrid.EntityId;
        m[k] = (m.ContainsKey(k) ? m[k] : 0) + score(b);
    }
    return m;
}

// Counting blocks of grids is unfortunately needed because the ingame API does not expose the grid block count.
// Feature request: https://support.keenswh.com/spaceengineers/pc/topic/feature-request-ingame-script-api-expose-grid-block-count

// countBlocks counts all blocks by walking the reachable grid positions.
// It walks all reachable blocks by position starting from the programmable block.
private int CountBlocks()
{
    var grid = Me.CubeGrid;
    var visit = new Stack<Vector3I>();
    visit.Push(Me.Position); // start from the programmable block
    visited.Clear();
    entities.Clear();
    int other = 0;
    while (visit.Count() > 0)
    {
        Vector3I p = visit.Pop();
        if (!grid.CubeExists(p)) continue;
        if (visited.Contains(p)) continue;
        visited.Add(p);
        visit.Push(new Vector3I(p.X + 1, p.Y, p.Z));
        visit.Push(new Vector3I(p.X, p.Y + 1, p.Z));
        visit.Push(new Vector3I(p.X, p.Y, p.Z + 1));
        visit.Push(new Vector3I(p.X - 1, p.Y, p.Z));
        visit.Push(new Vector3I(p.X, p.Y - 1, p.Z));
        visit.Push(new Vector3I(p.X, p.Y, p.Z - 1));
        IMySlimBlock block = grid.GetCubeBlock(p);
        if (block == null)
        {
            other++; // non-terminal blocks like armor, rotor, etc
        }
        else
        {
            entities.Add(block.FatBlock.EntityId);
        }
    }
    return entities.Count() + other;
}

// countBlocksPerGrid counts all blocks in all connected grids by walking the reachable grid positions.
List<IMyTerminalBlock> _list = new List<IMyTerminalBlock>();
HashSet<Vector3I> visited = new HashSet<Vector3I>();
List<long> entities = new List<long>();
private void CountBlocksPerGrid(Dictionary<long, IMyCubeGrid> grids, Dictionary<long, int> blocksPerGrid)
{
    var gridVisit = new Dictionary<long, Stack<Vector3I>>();

    // seed the walk with all terminal blocks across all connected grids
    // it means we don't count blocks in subgrids with no terminal blocks
    _list.Clear();
    GridTerminalSystem.GetBlocks(_list);
    foreach (var block in _list)
    {
        var k = block.CubeGrid.EntityId;
        if (!grids.ContainsKey(k))
        {
            grids[k] = block.CubeGrid;
            gridVisit[k] = new Stack<Vector3I>();
        }
        gridVisit[k].Push(block.Position);
    }

    // walk each grid independently
    foreach (var g in grids)
    {
        var id = g.Key;
        var grid = g.Value;
        var visit = gridVisit[id];
        visited.Clear();
        entities.Clear();
        int other = 0;
        while (visit.Count() > 0)
        {
            Vector3I p = visit.Pop();
            if (!grid.CubeExists(p)) continue;
            if (visited.Contains(p)) continue;
            visited.Add(p);
            visit.Push(new Vector3I(p.X + 1, p.Y, p.Z));
            visit.Push(new Vector3I(p.X, p.Y + 1, p.Z));
            visit.Push(new Vector3I(p.X, p.Y, p.Z + 1));
            visit.Push(new Vector3I(p.X - 1, p.Y, p.Z));
            visit.Push(new Vector3I(p.X, p.Y - 1, p.Z));
            visit.Push(new Vector3I(p.X, p.Y, p.Z - 1));
            IMySlimBlock block = grid.GetCubeBlock(p);
            if (block == null)
            {
                other++; // non-terminal blocks like armor, rotor, etc
            }
            else
            {
                entities.Add(block.FatBlock.EntityId);
            }
        }
        blocksPerGrid[id] = entities.Count() + other;
    }
}

private void ThreatScoreSingleGrid()
{
    float comm = BlocksThreat<IMyRadioAntenna>(b => 4);
    comm += BlocksThreat<IMyBeacon>(b => 3);
    float cargo = BlocksThreat<IMyCargoContainer>(b => ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 0.5f) + 0.5f);
    float controllers = BlocksThreat<IMyShipController>(b => 0.5f);
    float gravity = BlocksThreat<IMyGravityGenerator>(b => 2);
    gravity += BlocksThreat<IMyVirtualMass>(b => 2);
    //float weapons = BlocksThreat<IMyUserControllableGun>(b => 20);

    float weapons = BlocksThreat<IMyConveyorSorter>
        (
        b => wCFixed.Contains(b.BlockDefinition)
        ? b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 20) + 20
        : 0 : 0
        );

    weapons += BlocksThreat<IMySmallGatlingGun>
        (
        b => b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 20) + 20 : 0
        );

    weapons += BlocksThreat<IMySmallMissileLauncher>
        (
        b => b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 20) + 20 : 0
        );

    weapons += BlocksThreat<IMySmallMissileLauncherReload>
        (
        b => b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 20) + 20 : 0
        );

    weapons += BlocksThreat<IMyConveyorSorter>
        (
        b => wCTurret.Contains(b.BlockDefinition)
        ? b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 30) + 30
        : 0 : 0
        );

    weapons += BlocksThreat<IMyLargeMissileTurret>
        (
        b => b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 30) + 30 : 0
        );

    weapons += BlocksThreat<IMyLargeGatlingTurret>
        (
        b => b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 30) + 30 : 0
        );

    weapons += BlocksThreat<IMyLargeInteriorTurret>
        (
        b => b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 30) + 30 : 0
        );

    float jumpdrives = BlocksThreat<IMyJumpDrive>(b => 10);
    float mechanical = BlocksThreat<IMyMechanicalConnectionBlock>(b => 1);
    float medical = BlocksThreat<IMyMedicalRoom>(b => 10);
    medical += BlocksThreat<IMyTerminalBlock>(b => b.BlockDefinition.SubtypeName.StartsWith("SurvivalKit", StringComparison.OrdinalIgnoreCase) ? 10 : 0);
    float production = BlocksThreat<IMyProductionBlock>(b => ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 3) + 3);
    float thrusters = BlocksThreat<IMyThrust>(b => 2);
    float tools = BlocksThreat<IMyShipToolBase>(b => ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 2) + 2);
    float powerblocks = BlocksThreat<IMyPowerProducer>(b => 0.5f);
    float power = BlocksThreat<IMyPowerProducer>(b => (b as IMyPowerProducer).MaxOutput / 10, b => b.IsWorking);
    float blocks = (float)(totalBlocks > 0 ? totalBlocks : CountBlocks()) / 100;

    var grid = Me.CubeGrid;
    float sizescore = (float)Vector3D.Distance(grid.WorldAABB.Min, grid.WorldAABB.Max) / 4;
    float multiplier = grid.GridSizeEnum == MyCubeSize.Large ? 2.5f : 0.5f;
    if (grid.IsStatic) multiplier *= 0.75f;
    multiplier *= 0.70f; //Newer overall decrease multiplier in MES

    float score = (comm + cargo + controllers + gravity + weapons + jumpdrives + mechanical + medical + production + thrusters + tools + powerblocks + power + blocks + sizescore) * multiplier;

    Echo("Grid threat score: " + score);
    if (comm > 0) Echo(" - communications: " + comm * multiplier);
    if (cargo > 0) Echo(" - cargo: " + cargo * multiplier);
    if (controllers > 0) Echo(" - controllers: " + controllers * multiplier);
    if (gravity > 0) Echo(" - gravity: " + gravity * multiplier);
    if (weapons > 0) Echo(" - weapons: " + weapons * multiplier);
    if (jumpdrives > 0) Echo(" - jumpdrives: " + jumpdrives * multiplier);
    if (mechanical > 0) Echo(" - mechanical: " + mechanical * multiplier);
    if (medical > 0) Echo(" - medical: " + medical * multiplier);
    if (production > 0) Echo(" - production: " + production * multiplier);
    if (thrusters > 0) Echo(" - thrusters: " + thrusters * multiplier);
    if (tools > 0) Echo(" - tools: " + tools * multiplier);
    if (powerblocks > 0) Echo(" - powerblocks: " + powerblocks * multiplier);
    if (power > 0) Echo(" - powergen: " + power * multiplier);
    Echo(" - blocks: " + blocks * multiplier);
    Echo(" - size: " + sizescore * multiplier);
}

public void ThreatScoreMultiGrid()
{
    Dictionary<long, IMyCubeGrid> grids = new Dictionary<long, IMyCubeGrid>();
    Dictionary<long, int> blocksPerGrid = new Dictionary<long, int>();
    CountBlocksPerGrid(grids, blocksPerGrid);

    var comm = BlocksThreatPerGrid<IMyTerminalBlock>(b => 

    {
        if (b is IMyRadioAntenna)
        return 4;
        if (b is IMyBeacon)
		return 3;
		return 0;
    });
    var cargo = BlocksThreatPerGrid<IMyCargoContainer>(b => ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 0.5f) + 0.5f);
    var controllers = BlocksThreatPerGrid<IMyShipController>(b => 0.5f);
    // A bit of an experiment. -Trekker
    var gravity = BlocksThreatPerGrid<IMyTerminalBlock>(b =>
    {
        if (b is IMyGravityGenerator || b is IMyVirtualMass)
        return 2;
        return 0;
    });
    //gravity += (BlocksThreatPerGrid<IMyVirtualMass>(b => 2));

    // Trying the same approach, probably a lot more efficent this way too. - Trekker
    var weapons = BlocksThreatPerGrid<IMyTerminalBlock>(b =>
    {
        //if (b is IMyUserControllableGun)// Note, all of the vanilla types inherrit IMyUserControllableGun.
        //return 20;
        if (b is IMyConveyorSorter)
        {
            if (wCFixed.Contains(b.BlockDefinition))
            {
                return b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 20) + 20 : 0;
            }
            if (wCTurret.Contains(b.BlockDefinition))
            {
                return b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 30) + 30 : 0;
            }
            return 0;
        }
        if (b is IMyLargeTurretBase)// IMyLargeMissileTurret, IMyLargeGatlingTurret, and IMyLargeInteriorTurret all inherit from this.
        {
            return b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 30) + 30 : 0;
        }
        if (b is IMyUserControllableGun)// Remaining types are fixed weapons of IMySmallGatlingGun, IMySmallMissileLauncher, and IMySmallMissileLauncherReload type.
        {
            return b.GetInventory().MaxVolume > 0 ? ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 20) + 20 : 0;
        }

        return 0;
    });

    var jumpdrives = BlocksThreatPerGrid<IMyJumpDrive>(b => 10);
    var mechanical = BlocksThreatPerGrid<IMyMechanicalConnectionBlock>(b => 1);
    // Using the trick here as well. -Trekker
    var medical = BlocksThreatPerGrid<IMyMedicalRoom>(b =>
    {
        if (b is IMyMedicalRoom || b.BlockDefinition.SubtypeName.StartsWith("SurvivalKit", StringComparison.OrdinalIgnoreCase))
        return 10;
        return 0;
    });
    //medical += BlocksThreatPerGrid<IMyTerminalBlock>(b => b.BlockDefinition.SubtypeName.StartsWith("SurvivalKit", StringComparison.OrdinalIgnoreCase) ? 10 : 0);
    var production = BlocksThreatPerGrid<IMyProductionBlock>(b => ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 3) + 3);
    var thrusters = BlocksThreatPerGrid<IMyThrust>(b => 2);
    var tools = BlocksThreatPerGrid<IMyShipToolBase>(b => ((float)b.GetInventory().CurrentVolume / (float)b.GetInventory().MaxVolume * 2) + 2);
    var powerblocks = BlocksThreatPerGrid<IMyPowerProducer>(b => 0.5f);
    var powergen = BlocksThreatPerGrid<IMyPowerProducer>(b => (b as IMyPowerProducer).MaxOutput / 10, b => b.IsWorking);


    float total = 0;
    var details = new List<string>();
    foreach (var g in grids)
    {
        var k = g.Key;
        var grid = g.Value;
        float sizescore = (float)Vector3D.Distance(grid.WorldAABB.Min, grid.WorldAABB.Max) / 4;
        float multiplier = grid.GridSizeEnum == MyCubeSize.Large ? 2.5f : 0.5f;
        if (grid.IsStatic) multiplier *= 0.75f;
        multiplier *= 0.70f; //Newer overall decrease multiplier in MES


        float blocks = (float)blocksPerGrid[k] / 100;
        float score = multiplier * (
            (comm.ContainsKey(k) ? comm[k] : 0) +
            (cargo.ContainsKey(k) ? cargo[k] : 0) +
            (controllers.ContainsKey(k) ? controllers[k] : 0) +
            (gravity.ContainsKey(k) ? gravity[k] : 0) +
            (weapons.ContainsKey(k) ? weapons[k] : 0) +
            (jumpdrives.ContainsKey(k) ? jumpdrives[k] : 0) +
            (mechanical.ContainsKey(k) ? mechanical[k] : 0) +
            (medical.ContainsKey(k) ? medical[k] : 0) +
            (production.ContainsKey(k) ? production[k] : 0) +
            (thrusters.ContainsKey(k) ? thrusters[k] : 0) +
            (tools.ContainsKey(k) ? tools[k] : 0) +
            (powerblocks.ContainsKey(k) ? powerblocks[k] : 0) +
            (powergen.ContainsKey(k) ? powergen[k] : 0) +
            blocks + sizescore);
        total += score;

        details.Add(grid.CustomName + " score: " + score);
        if (comm.ContainsKey(k))if (comm[k]>0) details.Add(" - comm: " + comm[k]*multiplier);
        if (cargo.ContainsKey(k)) details.Add(" - cargo: " + cargo[k]*multiplier);
        if (controllers.ContainsKey(k)) details.Add(" - controllers: " + controllers[k]*multiplier);
        if (gravity.ContainsKey(k))if (gravity[k]>0) details.Add(" - gravity: " + gravity[k]*multiplier);
        if (weapons.ContainsKey(k))if (weapons[k]>0) details.Add(" - weapons: " + weapons[k]*multiplier);
        if (jumpdrives.ContainsKey(k)) details.Add(" - jumpdrives: " + jumpdrives[k]*multiplier);
        if (mechanical.ContainsKey(k)) details.Add(" - mechanical: " + mechanical[k]*multiplier);
        if (medical.ContainsKey(k))if (medical[k]>0) details.Add(" - medical: " + medical[k]*multiplier);
        if (production.ContainsKey(k)) details.Add(" - production: " + production[k]*multiplier);
        if (thrusters.ContainsKey(k)) details.Add(" - thrusters: " + thrusters[k]*multiplier);
        if (tools.ContainsKey(k)) details.Add(" - tools: " + tools[k]*multiplier);
        if (powerblocks.ContainsKey(k)) details.Add(" - powerblocks: " + powerblocks[k]*multiplier);
        if (powergen.ContainsKey(k)) details.Add(" - powergen: " + powergen[k]*multiplier);
        details.Add(blocksPerGrid[k]+" - blocks: " + blocks*multiplier);
        details.Add(" - size: " + sizescore*multiplier);


    }
    Echo("Total score: " + total);
    Echo(string.Join("\n", details));

}


public void Main()
{
    if (multiGrid) ThreatScoreMultiGrid();
    else ThreatScoreSingleGrid();
    Echo("Runtime: " + Math.Round(Runtime.LastRunTimeMs, 4) + "ms, " + Runtime.CurrentInstructionCount + " instrs");
}

}

/////////////////////////////

/// <summary>
    /// https://github.com/sstixrud/CoreSystems/blob/master/BaseData/Scripts/CoreSystems/Api/CoreSystemsPbApi.cs
    /// </summary>
public class WcPbApi
{
    private Action<ICollection<MyDefinitionId>> _getCoreWeapons;
    private Action<ICollection<MyDefinitionId>> _getCoreStaticLaunchers;
    private Action<ICollection<MyDefinitionId>> _getCoreTurrets;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, IDictionary<string, int>, bool> _getBlockWeaponMap;
    private Func<long, MyTuple<bool, int, int>> _getProjectilesLockedOn;
    private Action<Sandbox.ModAPI.Ingame.IMyTerminalBlock, IDictionary<MyDetectedEntityInfo, float>> _getSortedThreats;
    private Action<Sandbox.ModAPI.Ingame.IMyTerminalBlock, ICollection<Sandbox.ModAPI.Ingame.MyDetectedEntityInfo>> _getObstructions;
    private Func<long, int, MyDetectedEntityInfo> _getAiFocus;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, long, int, bool> _setAiFocus;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, int, MyDetectedEntityInfo> _getWeaponTarget;
    private Action<Sandbox.ModAPI.Ingame.IMyTerminalBlock, long, int> _setWeaponTarget;
    private Action<Sandbox.ModAPI.Ingame.IMyTerminalBlock, bool, int> _fireWeaponOnce;
    private Action<Sandbox.ModAPI.Ingame.IMyTerminalBlock, bool, bool, int> _toggleWeaponFire;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, int, bool, bool, bool> _isWeaponReadyToFire;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, int, float> _getMaxWeaponRange;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, ICollection<string>, int, bool> _getTurretTargetTypes;
    private Action<Sandbox.ModAPI.Ingame.IMyTerminalBlock, ICollection<string>, int> _setTurretTargetTypes;
    private Action<Sandbox.ModAPI.Ingame.IMyTerminalBlock, float> _setBlockTrackingRange;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, long, int, bool> _isTargetAligned;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, long, int, MyTuple<bool, Vector3D?>> _isTargetAlignedExtended;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, long, int, bool> _canShootTarget;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, long, int, Vector3D?> _getPredictedTargetPos;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, float> _getHeatLevel;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, float> _currentPowerConsumption;
    private Func<MyDefinitionId, float> _getMaxPower;
    private Func<long, bool> _hasGridAi;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, bool> _hasCoreWeapon;
    private Func<long, float> _getOptimalDps;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, int, string> _getActiveAmmo;
    private Action<Sandbox.ModAPI.Ingame.IMyTerminalBlock, int, string> _setActiveAmmo;
    private Action<Sandbox.ModAPI.Ingame.IMyTerminalBlock, int, Action<long, int, ulong, long, Vector3D, bool>> _monitorProjectile;
    private Action<Sandbox.ModAPI.Ingame.IMyTerminalBlock, int, Action<long, int, ulong, long, Vector3D, bool>> _unMonitorProjectile;
    private Func<ulong, MyTuple<Vector3D, Vector3D, float, float, long, string>> _getProjectileState;
    private Func<long, float> _getConstructEffectiveDps;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, long> _getPlayerController;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, int, Matrix> _getWeaponAzimuthMatrix;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, int, Matrix> _getWeaponElevationMatrix;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, long, bool, bool, bool> _isTargetValid;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, int, MyTuple<Vector3D, Vector3D>> _getWeaponScope;
    private Func<Sandbox.ModAPI.Ingame.IMyTerminalBlock, MyTuple<bool, bool>> _isInRange;

    public bool Activate(Sandbox.ModAPI.Ingame.IMyTerminalBlock pbBlock)
    {
        var dict = pbBlock.GetProperty("WcPbAPI")?.As<IReadOnlyDictionary<string, Delegate>>().GetValue(pbBlock);
        if (dict == null) throw new Exception("WcPbAPI failed to activate");
        return ApiAssign(dict);
    }

    public bool ApiAssign(IReadOnlyDictionary<string, Delegate> delegates)
    {
        if (delegates == null)
            return false;

        AssignMethod(delegates, "GetCoreWeapons", ref _getCoreWeapons);
        AssignMethod(delegates, "GetCoreStaticLaunchers", ref _getCoreStaticLaunchers);
        AssignMethod(delegates, "GetCoreTurrets", ref _getCoreTurrets);
        AssignMethod(delegates, "GetBlockWeaponMap", ref _getBlockWeaponMap);
        AssignMethod(delegates, "GetProjectilesLockedOn", ref _getProjectilesLockedOn);
        AssignMethod(delegates, "GetSortedThreats", ref _getSortedThreats);
        AssignMethod(delegates, "GetObstructions", ref _getObstructions);
        AssignMethod(delegates, "GetAiFocus", ref _getAiFocus);
        AssignMethod(delegates, "SetAiFocus", ref _setAiFocus);
        AssignMethod(delegates, "GetWeaponTarget", ref _getWeaponTarget);
        AssignMethod(delegates, "SetWeaponTarget", ref _setWeaponTarget);
        AssignMethod(delegates, "FireWeaponOnce", ref _fireWeaponOnce);
        AssignMethod(delegates, "ToggleWeaponFire", ref _toggleWeaponFire);
        AssignMethod(delegates, "IsWeaponReadyToFire", ref _isWeaponReadyToFire);
        AssignMethod(delegates, "GetMaxWeaponRange", ref _getMaxWeaponRange);
        AssignMethod(delegates, "GetTurretTargetTypes", ref _getTurretTargetTypes);
        AssignMethod(delegates, "SetTurretTargetTypes", ref _setTurretTargetTypes);
        AssignMethod(delegates, "SetBlockTrackingRange", ref _setBlockTrackingRange);
        AssignMethod(delegates, "IsTargetAligned", ref _isTargetAligned);
        AssignMethod(delegates, "IsTargetAlignedExtended", ref _isTargetAlignedExtended);
        AssignMethod(delegates, "CanShootTarget", ref _canShootTarget);
        AssignMethod(delegates, "GetPredictedTargetPosition", ref _getPredictedTargetPos);
        AssignMethod(delegates, "GetHeatLevel", ref _getHeatLevel);
        AssignMethod(delegates, "GetCurrentPower", ref _currentPowerConsumption);
        AssignMethod(delegates, "GetMaxPower", ref _getMaxPower);
        AssignMethod(delegates, "HasGridAi", ref _hasGridAi);
        AssignMethod(delegates, "HasCoreWeapon", ref _hasCoreWeapon);
        AssignMethod(delegates, "GetOptimalDps", ref _getOptimalDps);
        AssignMethod(delegates, "GetActiveAmmo", ref _getActiveAmmo);
        AssignMethod(delegates, "SetActiveAmmo", ref _setActiveAmmo);
        AssignMethod(delegates, "MonitorProjectile", ref _monitorProjectile);
        AssignMethod(delegates, "UnMonitorProjectile", ref _unMonitorProjectile);
        AssignMethod(delegates, "GetProjectileState", ref _getProjectileState);
        AssignMethod(delegates, "GetConstructEffectiveDps", ref _getConstructEffectiveDps);
        AssignMethod(delegates, "GetPlayerController", ref _getPlayerController);
        AssignMethod(delegates, "GetWeaponAzimuthMatrix", ref _getWeaponAzimuthMatrix);
        AssignMethod(delegates, "GetWeaponElevationMatrix", ref _getWeaponElevationMatrix);
        AssignMethod(delegates, "IsTargetValid", ref _isTargetValid);
        AssignMethod(delegates, "GetWeaponScope", ref _getWeaponScope);
        AssignMethod(delegates, "IsInRange", ref _isInRange);
        return true;
    }

    private void AssignMethod<T>(IReadOnlyDictionary<string, Delegate> delegates, string name, ref T field) where T : class
    {
        if (delegates == null)
        {
            field = null;
            return;
        }

        Delegate del;
        if (!delegates.TryGetValue(name, out del))
            throw new Exception($"{GetType().Name} :: Couldn't find {name} delegate of type {typeof(T)}");

        field = del as T;
        if (field == null)
            throw new Exception(
                $"{GetType().Name} :: Delegate {name} is not type {typeof(T)}, instead it's: {del.GetType()}");
    }

    public void GetAllCoreWeapons(ICollection<MyDefinitionId> collection) => _getCoreWeapons?.Invoke(collection);

    public void GetAllCoreStaticLaunchers(ICollection<MyDefinitionId> collection) =>
        _getCoreStaticLaunchers?.Invoke(collection);

    public void GetAllCoreTurrets(ICollection<MyDefinitionId> collection) => _getCoreTurrets?.Invoke(collection);

    public bool GetBlockWeaponMap(Sandbox.ModAPI.Ingame.IMyTerminalBlock weaponBlock, IDictionary<string, int> collection) =>
        _getBlockWeaponMap?.Invoke(weaponBlock, collection) ?? false;

    public MyTuple<bool, int, int> GetProjectilesLockedOn(long victim) =>
        _getProjectilesLockedOn?.Invoke(victim) ?? new MyTuple<bool, int, int>();

    public void GetSortedThreats(Sandbox.ModAPI.Ingame.IMyTerminalBlock pBlock, IDictionary<MyDetectedEntityInfo, float> collection) =>
        _getSortedThreats?.Invoke(pBlock, collection);
    public void GetObstructions(Sandbox.ModAPI.Ingame.IMyTerminalBlock pBlock, ICollection<Sandbox.ModAPI.Ingame.MyDetectedEntityInfo> collection) =>
        _getObstructions?.Invoke(pBlock, collection);
    public MyDetectedEntityInfo? GetAiFocus(long shooter, int priority = 0) => _getAiFocus?.Invoke(shooter, priority);

    public bool SetAiFocus(Sandbox.ModAPI.Ingame.IMyTerminalBlock pBlock, long target, int priority = 0) =>
        _setAiFocus?.Invoke(pBlock, target, priority) ?? false;

    public MyDetectedEntityInfo? GetWeaponTarget(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, int weaponId = 0) =>
        _getWeaponTarget?.Invoke(weapon, weaponId);

    public void SetWeaponTarget(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, long target, int weaponId = 0) =>
        _setWeaponTarget?.Invoke(weapon, target, weaponId);

    public void FireWeaponOnce(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, bool allWeapons = true, int weaponId = 0) =>
        _fireWeaponOnce?.Invoke(weapon, allWeapons, weaponId);

    public void ToggleWeaponFire(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, bool on, bool allWeapons, int weaponId = 0) =>
        _toggleWeaponFire?.Invoke(weapon, on, allWeapons, weaponId);

    public bool IsWeaponReadyToFire(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, int weaponId = 0, bool anyWeaponReady = true,
        bool shootReady = false) =>
        _isWeaponReadyToFire?.Invoke(weapon, weaponId, anyWeaponReady, shootReady) ?? false;

    public float GetMaxWeaponRange(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, int weaponId) =>
        _getMaxWeaponRange?.Invoke(weapon, weaponId) ?? 0f;

    public bool GetTurretTargetTypes(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, IList<string> collection, int weaponId = 0) =>
        _getTurretTargetTypes?.Invoke(weapon, collection, weaponId) ?? false;

    public void SetTurretTargetTypes(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, IList<string> collection, int weaponId = 0) =>
        _setTurretTargetTypes?.Invoke(weapon, collection, weaponId);

    public void SetBlockTrackingRange(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, float range) =>
        _setBlockTrackingRange?.Invoke(weapon, range);

    public bool IsTargetAligned(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, long targetEnt, int weaponId) =>
        _isTargetAligned?.Invoke(weapon, targetEnt, weaponId) ?? false;

    public MyTuple<bool, Vector3D?> IsTargetAlignedExtended(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, long targetEnt, int weaponId) =>
        _isTargetAlignedExtended?.Invoke(weapon, targetEnt, weaponId) ?? new MyTuple<bool, Vector3D?>();

    public bool CanShootTarget(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, long targetEnt, int weaponId) =>
        _canShootTarget?.Invoke(weapon, targetEnt, weaponId) ?? false;

    public Vector3D? GetPredictedTargetPosition(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, long targetEnt, int weaponId) =>
        _getPredictedTargetPos?.Invoke(weapon, targetEnt, weaponId) ?? null;

    public float GetHeatLevel(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon) => _getHeatLevel?.Invoke(weapon) ?? 0f;
    public float GetCurrentPower(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon) => _currentPowerConsumption?.Invoke(weapon) ?? 0f;
    public float GetMaxPower(MyDefinitionId weaponDef) => _getMaxPower?.Invoke(weaponDef) ?? 0f;
    public bool HasGridAi(long entity) => _hasGridAi?.Invoke(entity) ?? false;
    public bool HasCoreWeapon(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon) => _hasCoreWeapon?.Invoke(weapon) ?? false;
    public float GetOptimalDps(long entity) => _getOptimalDps?.Invoke(entity) ?? 0f;

    public string GetActiveAmmo(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, int weaponId) =>
        _getActiveAmmo?.Invoke(weapon, weaponId) ?? null;

    public void SetActiveAmmo(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, int weaponId, string ammoType) =>
        _setActiveAmmo?.Invoke(weapon, weaponId, ammoType);

    public void MonitorProjectileCallback(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, int weaponId, Action<long, int, ulong, long, Vector3D, bool> action) =>
        _monitorProjectile?.Invoke(weapon, weaponId, action);

    public void UnMonitorProjectileCallback(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, int weaponId, Action<long, int, ulong, long, Vector3D, bool> action) =>
        _unMonitorProjectile?.Invoke(weapon, weaponId, action);

    public MyTuple<Vector3D, Vector3D, float, float, long, string> GetProjectileState(ulong projectileId) =>
        _getProjectileState?.Invoke(projectileId) ?? new MyTuple<Vector3D, Vector3D, float, float, long, string>();

    public float GetConstructEffectiveDps(long entity) => _getConstructEffectiveDps?.Invoke(entity) ?? 0f;

    public long GetPlayerController(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon) => _getPlayerController?.Invoke(weapon) ?? -1;

    public Matrix GetWeaponAzimuthMatrix(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, int weaponId) =>
        _getWeaponAzimuthMatrix?.Invoke(weapon, weaponId) ?? Matrix.Zero;

    public Matrix GetWeaponElevationMatrix(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, int weaponId) =>
        _getWeaponElevationMatrix?.Invoke(weapon, weaponId) ?? Matrix.Zero;

    public bool IsTargetValid(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, long targetId, bool onlyThreats, bool checkRelations) =>
        _isTargetValid?.Invoke(weapon, targetId, onlyThreats, checkRelations) ?? false;

    public MyTuple<Vector3D, Vector3D> GetWeaponScope(Sandbox.ModAPI.Ingame.IMyTerminalBlock weapon, int weaponId) =>
        _getWeaponScope?.Invoke(weapon, weaponId) ?? new MyTuple<Vector3D, Vector3D>();
    // terminalBlock, Threat, Other, Something
    public MyTuple<bool, bool> IsInRange(Sandbox.ModAPI.Ingame.IMyTerminalBlock block) =>
        _isInRange?.Invoke(block) ?? new MyTuple<bool, bool>();
