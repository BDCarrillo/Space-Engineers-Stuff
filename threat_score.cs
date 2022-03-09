/*
Threat Score script calculates the Modular Encounters Spawner threat level score for the grid.
To preserve server performance it runs only once, if you change any blocks or grid connections, manually run again.
The score and details per block type (power, weapons, etc) are shown in the programmable block control panel.

Original Workshop link: https://steamcommunity.com/sharedfiles/filedetails/?id=2498906212

It is an approximation because ingame scripts cannot get the ModId of a block, so they cannot reliably double the threat score of modded blocks.
The script is only aware of a few modded blocks from Draconis Impossible Extended (https://wiki.sigmadraconis.games/doku.php?id=di:draconis_impossible).

Finally, remember the total threat score is calculated for all grids in a given sphere radius (other players, unknown signals, etc).

Modular Encounters Spawner mod: https://steamcommunity.com/workshop/filedetails/?id=1521905890

Original work by StalkR, modified by BDCarrillo as permitted by original license: https://github.com/StalkR/Space-Engineers-Stuff/blob/master/LICENSE
*/

// Calculate the threat score of the current grid and connected grids (true), or only the current grid (false).
bool multiGrid = false;

// If you get "error: Script execution terminated, script is too complex" it's because the grid is too large for
// counting all the non-terminal blocks, because unfortunately the game doesn't expose them (see below).
// In that case:
//  - switch to `multiGrid = false` above
//  - open the 'info' tab for the grid and write the total number of blocks below
int totalBlocks = 0;

public Program() {
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

private bool isMod(IMyTerminalBlock block) {
    var key = block.BlockDefinition.TypeId.ToString() + "/" + block.BlockDefinition.SubtypeId.ToString();
    return moddedBlocks.Contains(key);
}

private float blocksThreat<T>(Func<IMyTerminalBlock, float> score, Func<IMyTerminalBlock, Boolean> collect = null) where T : class {
    var list = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType<T>(list,
        b => b.CubeGrid.EntityId == Me.CubeGrid.EntityId && b.IsFunctional && (collect == null || collect(b)));
    return list.Sum(b => score(b));
}

private Dictionary<long, float> blocksThreatPerGrid<T>(
        Func<IMyTerminalBlock, float> score,
        Func<IMyTerminalBlock, Boolean> collect = null
    ) where T : class {
    var list = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType<T>(list, b => b.IsFunctional && (collect == null || collect(b)));
    var m = new Dictionary<long, float>();
    foreach (var b in list) {
        var k = b.CubeGrid.EntityId;
        m[k] = (m.ContainsKey(k) ? m[k] : 0) + score(b);
    }
    return m;
}

// Counting blocks of grids is unfortunately needed because the ingame API does not expose the grid block count.
// Feature request: https://support.keenswh.com/spaceengineers/pc/topic/feature-request-ingame-script-api-expose-grid-block-count

// countBlocks counts all blocks by walking the reachable grid positions.
// It walks all reachable blocks by position starting from the programmable block.
private int countBlocks() {
    var grid = Me.CubeGrid;
    var visit = new Stack<Vector3I>();
    visit.Push(Me.Position); // start from the programmable block
    var visited = new HashSet<string>();
    var entities = new HashSet<long>();
    int other = 0;
    while (visit.Count() > 0) {
        Vector3I p = visit.Pop();
        if (!grid.CubeExists(p)) continue;
        var k = p.X + "/" + p.Y + "/" + p.Z;
        if (visited.Contains(k)) continue;
        visited.Add(k);
        visit.Push(new Vector3I(p.X+1, p.Y, p.Z));
        visit.Push(new Vector3I(p.X, p.Y+1, p.Z));
        visit.Push(new Vector3I(p.X, p.Y, p.Z+1));
        visit.Push(new Vector3I(p.X-1, p.Y, p.Z));
        visit.Push(new Vector3I(p.X, p.Y-1, p.Z));
        visit.Push(new Vector3I(p.X, p.Y, p.Z-1));
        IMySlimBlock block = grid.GetCubeBlock(p);
        if (block == null) {
            other++; // non-terminal blocks like armor, rotor, etc
        } else {
            entities.Add(block.FatBlock.EntityId);
        }
    }
    return entities.Count() + other;
}

// countBlocksPerGrid counts all blocks in all connected grids by walking the reachable grid positions.
private void countBlocksPerGrid(Dictionary<long, IMyCubeGrid> grids, Dictionary<long, int> blocksPerGrid) {
    var gridVisit = new Dictionary<long, Stack<Vector3I>>();

    // seed the walk with all terminal blocks across all connected grids
    // it means we don't count blocks in subgrids with no terminal blocks
    var list = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(list);
    foreach (var block in list) {
        var k = block.CubeGrid.EntityId;
        if (!grids.ContainsKey(k)) {
            grids[k] = block.CubeGrid;
            gridVisit[k] = new Stack<Vector3I>();
        }
        gridVisit[k].Push(block.Position);
    }

    // walk each grid independently
    foreach (var g in grids) {
        var id = g.Key;
        var grid = g.Value;
        var visit = gridVisit[id];
        var visited = new HashSet<string>();
        var entities = new HashSet<long>();
        int other = 0;
        while (visit.Count() > 0) {
            Vector3I p = visit.Pop();
            if (!grid.CubeExists(p)) continue;
            var k = p.X + "/" + p.Y + "/" + p.Z;
            if (visited.Contains(k)) continue;
            visited.Add(k);
            visit.Push(new Vector3I(p.X+1, p.Y, p.Z));
            visit.Push(new Vector3I(p.X, p.Y+1, p.Z));
            visit.Push(new Vector3I(p.X, p.Y, p.Z+1));
            visit.Push(new Vector3I(p.X-1, p.Y, p.Z));
            visit.Push(new Vector3I(p.X, p.Y-1, p.Z));
            visit.Push(new Vector3I(p.X, p.Y, p.Z-1));
            IMySlimBlock block = grid.GetCubeBlock(p);
            if (block == null) {
                other++; // non-terminal blocks like armor, rotor, etc
            } else {
                entities.Add(block.FatBlock.EntityId);
            }
        }
        blocksPerGrid[id] = entities.Count() + other;
    }
}

private void threatScoreSingleGrid() {
    float antenna = blocksThreat<IMyRadioAntenna>(b => 4);
    float beacon = blocksThreat<IMyBeacon>(b => 3);
    float cargo = blocksThreat<IMyCargoContainer>(b => 0.5f); //need inventory calc
    float controllers = blocksThreat<IMyShipController>(b => 0.5f);
    float gravity = blocksThreat<IMyGravityGenerator>(b => 2);
    gravity += blocksThreat<IMyVirtualMass>(b => 2);
    float weapons = blocksThreat<IMyUserControllableGun>(b => 20);
    //TODO: add WC fixed weapons
    float jumpdrives = blocksThreat<IMyJumpDrive>(b => 10);
    float mechanical = blocksThreat<IMyMechanicalConnectionBlock>(b => 1);
    float medical = blocksThreat<IMyMedicalRoom>(b => 10);
    //TODO: need to account for survival kits += blocksThreat<>(b =>10);
    float production = blocksThreat<IMyProductionBlock>(b => 3); //need inventory calc  
    float thrusters = blocksThreat<IMyThrust>(b => 2);
    float tools = blocksThreat<IMyShipToolBase>(b => 2);
    //TODO: add WC turrets at 30 each   
    float powerblocks = blocksThreat<IMyPowerProducer>(b => 0.5f);
    
    float power = blocksThreat<IMyPowerProducer>(b => (b as IMyPowerProducer).MaxOutput / 10, b => b.IsWorking);

    float blocks = (float)(totalBlocks > 0 ? totalBlocks : countBlocks()) / 100;

    var grid=Me.CubeGrid;
    float sizescore = (float)Vector3D.Distance(grid.WorldAABB.Min, grid.WorldAABB.Max)/4;
    float multiplier = grid.GridSizeEnum == MyCubeSize.Large ? 2.5f : 0.5f;
    if(grid.IsStatic) multiplier *=0.75f;
    
    
    multiplier *= 0.70f; //Newer overall decrease multiplier in MES
    float score = (antenna+beacon+cargo+controllers+gravity+weapons+jumpdrives+mechanical+medical+production+thrusters+tools+powerblocks+power+blocks+sizescore) * multiplier;

    Echo("Grid threat score: " + score);
    Echo(" - antenna: " + antenna* multiplier);
    Echo(" - beacon: " + beacon* multiplier);
    Echo(" - cargo: " + cargo* multiplier);
    Echo(" - controllers: " + controllers* multiplier);
    Echo(" - gravity: " + gravity* multiplier);
    Echo(" - fixed weapons: " + weapons* multiplier);    
    Echo(" - jumpdrives: " + jumpdrives* multiplier);
    Echo(" - mechanical: " + mechanical* multiplier);
    Echo(" - medical: " + medical* multiplier);
    Echo(" - production: " + production* multiplier);    
    Echo(" - thrusters: " + thrusters* multiplier);
    Echo(" - tools: " + tools* multiplier);
    Echo(" - powerblocks: " + powerblocks* multiplier);
    Echo(" - powergen: " + power* multiplier);
    Echo(" - blocks: " + blocks* multiplier);
    Echo(" - size: " + sizescore* multiplier);
}

public void threatScoreMultiGrid() {
    Dictionary<long, IMyCubeGrid> grids = new Dictionary<long, IMyCubeGrid>();
    Dictionary<long, int> blocksPerGrid = new Dictionary<long, int>();
    countBlocksPerGrid(grids, blocksPerGrid);
    
    //TODO: Update all values for MultiGrid
    var power = blocksThreatPerGrid<IMyPowerProducer>(b => (b as IMyPowerProducer).MaxOutput / 10, b => b.IsWorking);
    var weapons = blocksThreatPerGrid<IMyUserControllableGun>(b => 5);
    var production = blocksThreatPerGrid<IMyProductionBlock>(b => isMod(b) ? 3 : 1.5f);
    var tools = blocksThreatPerGrid<IMyShipToolBase>(b => 1);
    var thrusters = blocksThreatPerGrid<IMyThrust>(b => 1);
    var cargo = blocksThreatPerGrid<IMyCargoContainer>(b => 0.5f);
    var antenna = blocksThreatPerGrid<IMyRadioAntenna>(b => 4);
    var beacon = blocksThreatPerGrid<IMyBeacon>(b => isMod(b) ? 6 : 3);

    float total = 0;
    var details = new List<string>();
    foreach (var g in grids) {
        var k = g.Key;
        var grid = g.Value;

        float multiplier = grid.GridSizeEnum == MyCubeSize.Large ? 2.5f : 0.5f;
        float blocks = (float)blocksPerGrid[k] / 100;
        float score = multiplier * (
            (power.ContainsKey(k) ? power[k] : 0) +
            (weapons.ContainsKey(k) ? weapons[k] : 0) +
            (production.ContainsKey(k) ? production[k] : 0) +
            (tools.ContainsKey(k) ? tools[k] : 0) +
            (thrusters.ContainsKey(k) ? thrusters[k] : 0) +
            (cargo.ContainsKey(k) ? cargo[k] : 0) +
            (antenna.ContainsKey(k) ? antenna[k] : 0) +
            (beacon.ContainsKey(k) ? beacon[k] : 0) +
            blocks);
        total += score;

        details.Add("Threat score for " + grid.CustomName + ": " + score);
        if (power.ContainsKey(k)) details.Add(" - power: " + power[k]);
        if (weapons.ContainsKey(k)) details.Add(" - weapons: " + weapons[k]);
        if (production.ContainsKey(k)) details.Add(" - production: " + production[k]);
        if (tools.ContainsKey(k)) details.Add(" - tools: " + tools[k]);
        if (thrusters.ContainsKey(k)) details.Add(" - thrusters: " + thrusters[k]);
        if (cargo.ContainsKey(k)) details.Add(" - cargo: " + cargo[k]);
        if (antenna.ContainsKey(k)) details.Add(" - antenna: " + antenna[k]);
        if (beacon.ContainsKey(k)) details.Add(" - beacon: " + beacon[k]);
        details.Add(" - blocks: " + blocks);
        details.Add(" - " + (multiplier == 2.5f ? "large" : "small" ) + " grid multiplier: " + multiplier);
    }

    Echo("Threat score: " + total);
    Echo(string.Join("\n", details));
}


public void Main() {
    if (multiGrid) threatScoreMultiGrid();
    else threatScoreSingleGrid();
    Echo("Runtime: " + Math.Round(Runtime.LastRunTimeMs, 4) + "ms, " + Runtime.CurrentInstructionCount + " instrs");
}
