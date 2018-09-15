using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace TargetLeading
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Core : MySessionComponentBase
    {   
        private List<IMyCubeGrid> Grids = new List<IMyCubeGrid>();
        private Dictionary<long, IMyGps> gpss = new Dictionary<long, IMyGps>();

        private bool WasInTurretLastFrame = false;


        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            MyAPIGateway.Entities.OnEntityAdd += AddGrid;
            MyAPIGateway.Entities.OnEntityRemove += RemoveGrid;
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Entities.OnEntityAdd -= AddGrid;
            MyAPIGateway.Entities.OnEntityRemove -= RemoveGrid;
        }

        private void AddGrid(IMyEntity ent)
        {
            if (ent is IMyCubeGrid)
            {
                Grids.Add(ent as IMyCubeGrid);
            }
        }

        private void RemoveGrid(IMyEntity ent)
        {
            if (ent is IMyCubeGrid && Grids.Contains(ent))
            {
                Grids.Remove(ent as IMyCubeGrid);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            IMyLargeTurretBase turret = MyAPIGateway.Session?.Player?.Controller?.ControlledEntity?.Entity as IMyLargeTurretBase;

            if (turret == null)
            {
                ClearGPSMarkers();
                return;
            }

            MyAPIGateway.Utilities.ShowNotification($"In Turret", 1);

            WasInTurretLastFrame = true;
            IMyGunObject<MyGunBase> gunBase = turret as IMyGunObject<MyGunBase>;

            if (gunBase.GunBase.CurrentAmmoDefinition == null)
            {
                ClearGPSMarkers();
                return;
            }

            MyAPIGateway.Utilities.ShowNotification($"Has Ammo Definition", 1);

            Vector3D turretLoc = turret.GetPosition();
            float speed = gunBase.GunBase.CurrentAmmoDefinition.DesiredSpeed;
            float range = gunBase.GunBase.CurrentAmmoDefinition.MaxTrajectory;
            float rangeSquared = range * range;
            BoundingSphereD sphere = new BoundingSphereD(turretLoc, range);

            MyAPIGateway.Utilities.ShowNotification($"Speed: {speed} Range: {range}", 1);

            foreach (IMyCubeGrid grid in Grids)
            {
                if (grid.EntityId != turret.CubeGrid.EntityId
                && Vector3D.DistanceSquared(grid.GetPosition(), turretLoc) < rangeSquared
                //&& MyAPIGateway.Players.GetPlayerControllingEntity(e) != null
                ) continue;

                if (grid.Physics == null) continue;

                DrawDot(grid.EntityId, grid.GetPosition());
            }
        }

        private void DrawDot(long gridId, Vector3D target)
        {
            if (!gpss.ContainsKey(gridId))
            {
                gpss.Add(gridId, MyAPIGateway.Session.GPS.Create("", "", target, true));
                MyAPIGateway.Session.GPS.AddLocalGps(gpss[gridId]);
            }

            gpss[gridId].Coords = target;
        }

        private void ClearGPSMarkers()
        {
            if (!WasInTurretLastFrame) return;

            foreach (IMyGps gps in gpss.Values)
            {
                MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
            }
            gpss.Clear();

            WasInTurretLastFrame = false;
        }
    }
}
