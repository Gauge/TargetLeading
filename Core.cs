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
        private List<IMyCubeGrid> _grids = new List<IMyCubeGrid>();
        private Dictionary<long, IMyGps> _gpsPoints = new Dictionary<long, IMyGps>();
        private IMyGunObject<MyGunBase> _gunBase;
        private bool _wasInTurretLastFrame = false;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Entities.OnEntityAdd += AddGrid;
            MyAPIGateway.Entities.OnEntityRemove += RemoveGrid;
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Entities.OnEntityAdd -= AddGrid;
            MyAPIGateway.Entities.OnEntityRemove -= RemoveGrid;
        }

        private void AddGrid(IMyEntity ent)
        {
            var grid = ent as IMyCubeGrid;
            if (grid != null)
            {
                _grids.Add(grid);
            }
        }

        private void RemoveGrid(IMyEntity ent)
        {
            var grid = ent as IMyCubeGrid;
            if (grid != null && _grids.Contains(grid))
            {
                _grids.Remove(grid);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            IMyLargeTurretBase turret = MyAPIGateway.Session?.Player?.Controller?.ControlledEntity?.Entity as IMyLargeTurretBase;

            if (turret == null)
            {
                ClearGPS();
                return;
            }

            _wasInTurretLastFrame = true;
            _gunBase = turret as IMyGunObject<MyGunBase>;

            if (_gunBase.GunBase.CurrentAmmoDefinition == null)
            {
                ClearGPS();
                return;
            }

            Vector3D turretLoc = turret.GetPosition();
            float projectileSpeed = _gunBase.GunBase.CurrentAmmoDefinition.DesiredSpeed;
            float projectileRange = _gunBase.GunBase.CurrentAmmoDefinition.MaxTrajectory;
            float projectileRangeSquared = projectileRange * projectileRange;

            foreach (IMyCubeGrid grid in _grids)
            {
                if (grid.Physics == null)
                {
                    RemoveGPS(grid.EntityId);
                    continue;
                }

                IMyPlayer p = MyAPIGateway.Players.GetPlayerControllingEntity(grid);
                Vector3D gridLoc = grid.WorldAABB.Center;

                if (grid.EntityId == turret.CubeGrid.EntityId
                    || Vector3D.DistanceSquared(gridLoc, turretLoc) > projectileRangeSquared
                    || !GridHasHostileOwners(grid)
                    || p == null
                    || p.GetRelationTo(MyAPIGateway.Session.Player.IdentityId) != MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    RemoveGPS(grid.EntityId);
                    continue;
                }

                Vector3D interceptPoint = CalculateProjectileIntercept(
                    projectileSpeed,
                    turret.CubeGrid.Physics.LinearVelocity,
                    turretLoc,
                    grid.Physics.LinearVelocity,
                    gridLoc);

                AddGPS(grid.EntityId, interceptPoint);
            }
        }

        public static bool GridHasHostileOwners(IMyCubeGrid grid)
        {
            var gridOwners = grid.BigOwners;
            foreach (var pid in gridOwners)
            {
                MyRelationsBetweenPlayerAndBlock relation = MyAPIGateway.Session.Player.GetRelationTo(pid);
                if (relation == MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    return true;
                }
            }
            return false;
        }

        // Whip's CalculateProjectileIntercept Method v2
        // Uses vector math as opposed to the quadratic equation
        private static Vector3D CalculateProjectileIntercept(
            double projectileSpeed,
            Vector3D shooterVelocity,
            Vector3D shooterPosition,
            Vector3D targetVelocity,
            Vector3D targetPos)
        {
            var directHeading = targetPos - shooterPosition;
            var directHeadingNorm = Vector3D.Normalize(directHeading);

            var relativeVelocity = targetVelocity - shooterVelocity;

            var parallelVelocity = relativeVelocity.Dot(directHeadingNorm) * directHeadingNorm;
            var normalVelocity = relativeVelocity - parallelVelocity;

            var diff = projectileSpeed * projectileSpeed - normalVelocity.LengthSquared();
            if (diff < 0)
                return normalVelocity;

            return Math.Sqrt(diff) * directHeadingNorm + normalVelocity;
        }

        private void AddGPS(long gridId, Vector3D target)
        {
            if (!_gpsPoints.ContainsKey(gridId))
            {
                _gpsPoints.Add(gridId, MyAPIGateway.Session.GPS.Create(gridId.ToString(), "", target, true));
                MyAPIGateway.Session.GPS.AddLocalGps(_gpsPoints[gridId]);
                MyVisualScriptLogicProvider.SetGPSColor(gridId.ToString(), Color.Orange);
                _gpsPoints[gridId].Name = "";
            }

            _gpsPoints[gridId].Coords = target;
        }

        private void ClearGPS()
        {
            if (!_wasInTurretLastFrame)
                return;

            foreach (IMyGps gps in _gpsPoints.Values)
            {
                MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
            }
            _gpsPoints.Clear();

            _wasInTurretLastFrame = false;
        }

        private void RemoveGPS(long id)
        {
            if (_gpsPoints.ContainsKey(id))
            {
                MyAPIGateway.Session.GPS.RemoveLocalGps(_gpsPoints[id]);
                _gpsPoints.Remove(id);
            }
        }
    }
}