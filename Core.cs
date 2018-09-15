using Sandbox.Game;
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

            WasInTurretLastFrame = true;
            IMyGunObject<MyGunBase> gunBase = turret as IMyGunObject<MyGunBase>;

            if (gunBase.GunBase.CurrentAmmoDefinition == null)
            {
                ClearGPSMarkers();
                return;
            }

            Vector3D turretLoc = turret.GetPosition();
            float speed = gunBase.GunBase.CurrentAmmoDefinition.DesiredSpeed;
            float range = gunBase.GunBase.CurrentAmmoDefinition.MaxTrajectory;
            float rangeSquared = range * range;
            BoundingSphereD sphere = new BoundingSphereD(turretLoc, range);

            foreach (IMyCubeGrid grid in Grids)
            {
                IMyPlayer p = MyAPIGateway.Players.GetPlayerControllingEntity(grid);

                if (grid.EntityId == turret.CubeGrid.EntityId
                || Vector3D.DistanceSquared(grid.GetPosition(), turretLoc) > rangeSquared
                || p == null
                || p.GetRelationTo(MyAPIGateway.Session.Player.IdentityId) != MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    RemoveGPS(grid.EntityId);
                    continue;
                }

                bool isSubgrid = false;

                if (isSubgrid || grid.Physics == null)
                {
                    RemoveGPS(grid.EntityId);
                    continue;
                }

                Vector3D gridLoc = grid.PositionComp.GetPosition();
                float t = (float)(Vector3D.Distance(turretLoc, gridLoc) / speed);

                DrawDot(grid.EntityId, (gridLoc + t * grid.Physics.LinearVelocity));
            }
        }

        private void DrawDot(long gridId, Vector3D target)
        {
            if (!gpss.ContainsKey(gridId))
            {
                gpss.Add(gridId, MyAPIGateway.Session.GPS.Create(gridId.ToString(), "", target, true));
                MyAPIGateway.Session.GPS.AddLocalGps(gpss[gridId]);
                MyVisualScriptLogicProvider.SetGPSColor(gridId.ToString(), Color.Orange);
                gpss[gridId].Name = "";
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

        private void RemoveGPS(long id)
        {
            if (gpss.ContainsKey(id))
            {
                MyAPIGateway.Session.GPS.RemoveLocalGps(gpss[id]);
                gpss.Remove(id);
            }
        }
    }
}
