using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
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
            float projectileRange = _gunBase.GunBase.CurrentAmmoDefinition.MaxTrajectory + 100;
            float projectileRangeSquared = projectileRange * projectileRange;

            foreach (IMyCubeGrid grid in _grids)
            {
                if (grid.Physics == null)
                {
                    RemoveGPS(grid.EntityId);
                    continue;
                }

                Vector3D gridLoc = grid.WorldAABB.Center;

                if (grid.EntityId == turret.CubeGrid.EntityId
                    || Vector3D.DistanceSquared(gridLoc, turretLoc) > projectileRangeSquared
                    || !GridHasHostileOwners(grid))
                {
                    RemoveGPS(grid.EntityId);
                    continue;
                }

                Vector3D interceptPoint = CalculateProjectileInterceptPosition(
                    projectileSpeed,
                    turret.CubeGrid.Physics.LinearVelocity,
                    turretLoc,
                    grid.Physics.LinearVelocity,
                    gridLoc, 10);

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

        // Whip's CalculateProjectileInterceptPosition Method
        // Uses vector math as opposed to the quadratic equation
        private static Vector3D CalculateProjectileInterceptPosition(
            double projectileSpeed,
            Vector3D shooterVelocity,
            Vector3D shooterPosition,
            Vector3D targetVelocity,
            Vector3D targetPos,
            double interceptPointMultiplier = 1)
        {
            var directHeading = targetPos - shooterPosition;
            var directHeadingNorm = Vector3D.Normalize(directHeading);

            var relativeVelocity = targetVelocity - shooterVelocity;

            var parallelVelocity = relativeVelocity.Dot(directHeadingNorm) * directHeadingNorm;
            var normalVelocity = relativeVelocity - parallelVelocity;

            var diff = projectileSpeed * projectileSpeed - normalVelocity.LengthSquared();
            if (diff < 0)
                return normalVelocity;

            var projectileForwardVelocity = Math.Sqrt(diff) * directHeadingNorm;

            var timeToIntercept = interceptPointMultiplier * Math.Abs(Vector3D.Dot(directHeading, directHeadingNorm)) / Vector3D.Dot(projectileForwardVelocity, directHeadingNorm);

            //// poked this in here cause im lazy - Gauge
            //var drawendpoint = shooterPosition + (projectileForwardVelocity + normalVelocity);
            //MyTransparentGeometry.AddLineBillboard(
            //    MyStringId.GetOrCompute("ContainerBorder"),
            //    Color.Orange.ToVector4(),
            //    targetPos,
            //    -Vector3D.Normalize(targetPos - drawendpoint),
            //    (float)Vector3D.Distance(targetPos, drawendpoint),
            //    0.05f);

            //MyTransparentGeometry.AddPointBillboard(
            //    MyStringId.GetOrCompute("ContainerBorder"),
            //    Color.Orange.ToVector4(),
            //    drawendpoint,
            //    0.1f,
            //    0.05f);

            return shooterPosition + timeToIntercept * (projectileForwardVelocity + normalVelocity);
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
