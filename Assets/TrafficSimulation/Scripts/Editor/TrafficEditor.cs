﻿// Traffic Simulation
// https://github.com/mchrbn/unity-traffic-simulation

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace TrafficSimulation{

    [CustomEditor(typeof(TrafficSystem))]
    public class TrafficEditor : Editor {

        private TrafficSystem wps;
        
        //References for moving a waypoint
        private Vector3 lastPoint;
        private Waypoint lastWaypoint;
        
        [MenuItem("Component/Traffic Simulation/Create Traffic Objects")]
        static void CreateTraffic(){
            GameObject mainGo = new GameObject("Traffic System");
            mainGo.transform.position = Vector3.zero;
            mainGo.AddComponent<TrafficSystem>();

            GameObject segmentsGo = new GameObject("Segments");
            segmentsGo.transform.position = Vector3.zero;
            segmentsGo.transform.SetParent(mainGo.transform);

            GameObject intersectionsGo = new GameObject("Intersections");
            intersectionsGo.transform.position = Vector3.zero;
            intersectionsGo.transform.SetParent(mainGo.transform);
        }

        void OnEnable(){
            wps = target as TrafficSystem;
        }

        private void OnSceneGUI() {
            Event e = Event.current;
            if (e == null) return;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit) && e.type == EventType.MouseDown && e.button == 0) {
                //Add a new waypoint on mouseclick + shift
                if (e.shift) {
                    if (wps.curSegment == null) {
                        return;
                    }

                    AddWaypoint(hit.point);
                }

                //Create a segment + add a new waypoint on mouseclick + ctrl
                else if (e.control) {
                    AddSegment(hit.point);
                    AddWaypoint(hit.point);
                }

                //Create an intersection type
                else if (e.alt) {
                    AddIntersection(hit.point);
                }
            }

            //Set waypoint system as the selected gameobject in hierarchy
            Selection.activeGameObject = wps.gameObject;

            bool moved = false;

            //Handle the selected waypoint
            if (lastWaypoint != null) {
                //Uses a endless plain for the ray to hit
                Plane plane = new Plane(Vector3.up.normalized, lastWaypoint.transform.position);
                plane.Raycast(ray, out float dst);
                Vector3 hitPoint = ray.GetPoint(dst);

                //Reset lastPoint if the mouse button is pressed down the first time
                if (e.type == EventType.MouseDown && e.button == 0) {
                    lastPoint = hitPoint;
                }

                //Move the selected waypoint
                if (e.type == EventType.MouseDrag && e.button == 0) {
                    Vector3 realDPos = new Vector3(hitPoint.x - lastPoint.x, 0, hitPoint.z - lastPoint.z);
                    moved = true;

                    lastWaypoint.transform.position += realDPos;
                    lastPoint = hitPoint;
                }

                //Draw a Sphere
                Handles.SphereHandleCap(0, lastWaypoint.transform.position, Quaternion.identity, 1, EventType.Repaint);
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                SceneView.RepaintAll();
            }

            //Look if the users mouse is over a waypoint
            List<RaycastHit> hits = Physics.RaycastAll(ray, float.MaxValue, LayerMask.GetMask("UnityEditor")).ToList();

            //Set the current hovering waypoint
            if (lastWaypoint == null && hits.Exists(i => i.collider.CompareTag("Waypoint"))) {
                lastWaypoint = hits.First(i => i.collider.CompareTag("Waypoint")).collider.GetComponent<Waypoint>();
            } 
            
            //Only reset if the current waypoint was not used
            else if (e.type == EventType.MouseMove && !moved) {
                lastWaypoint = null;
            }

            //Tell Unity that something changed and the scene has to be saved
            if (moved && !EditorUtility.IsDirty(target)) {
                EditorUtility.SetDirty(target);
            }
        }

        public override void OnInspectorGUI(){
            EditorGUI.BeginChangeCheck();
            
            //Editor properties
            EditorGUILayout.LabelField("Guizmo Config", EditorStyles.boldLabel);
            wps.hideGuizmos = EditorGUILayout.Toggle("Hide Guizmos", wps.hideGuizmos);
            
            //ArrowDrawType selection
            wps.arrowDrawType = (ArrowDraw) EditorGUILayout.EnumPopup("Arrow Draw Type", wps.arrowDrawType);
            EditorGUI.indentLevel++;

            switch (wps.arrowDrawType) {
                case ArrowDraw.FixedCount:
                    wps.arrowCount = Mathf.Max(1, EditorGUILayout.IntField("Count", wps.arrowCount));
                    break;
                case ArrowDraw.ByLength:
                    wps.arrowDistance = EditorGUILayout.FloatField("Distance Between Arrows", wps.arrowDistance);
                    break;
                case ArrowDraw.Off:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
            if (wps.arrowDrawType != ArrowDraw.Off) {
                wps.arrowSizeWaypoint = EditorGUILayout.FloatField("Arrow Size Waypoint", wps.arrowSizeWaypoint);
                wps.arrowSizeIntersection = EditorGUILayout.FloatField("Arrow Size Intersection", wps.arrowSizeIntersection);
            }
            
            EditorGUI.indentLevel--;
            
            //System Config
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("System Config", EditorStyles.boldLabel);
            wps.segDetectThresh = EditorGUILayout.FloatField("Segment Detection Threshold", wps.segDetectThresh);
            
            //Helper
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Ctrl + Left Click to create a new segment\nShift + Left Click to create a new waypoint.\nAlt + Left Click to create a new intersection", MessageType.Info);
            EditorGUILayout.HelpBox("Reminder: The cars will follow the point depending on the sequence you added them. (go to the 1st waypoint added, then to the second, etc.)", MessageType.Info);


            //Rename waypoints if some have been deleted
            if(GUILayout.Button("Re-Structure Traffic System")){
                RestructureSystem();
            }

            //Repaint the scene if values have been edited
            if (EditorGUI.EndChangeCheck()) {
                SceneView.RepaintAll();
            }

            serializedObject.ApplyModifiedProperties();
        }

        void AddWaypoint(Vector3 position){
            GameObject go = new GameObject("Waypoint-" + wps.curSegment.waypoints.Count);
            go.transform.position = position;
            go.transform.SetParent(wps.curSegment.transform);

            Waypoint wp = go.AddComponent<Waypoint>();
            wp.Refresh(wps.curSegment.waypoints.Count, wps.curSegment);

            wps.curSegment.waypoints.Add(wp);
        }

        void AddSegment(Vector3 position){
            int segId = wps.segments.Count;
            GameObject segGo = new GameObject("Segment-" + segId);
            segGo.transform.position = position;
            segGo.transform.SetParent(wps.transform.GetChild(0).transform);
            wps.curSegment = segGo.AddComponent<Segment>();
            wps.curSegment.id = segId;
            wps.curSegment.waypoints = new List<Waypoint>();
            wps.curSegment.nextSegments = new List<Segment>();
            wps.segments.Add(wps.curSegment);
        }

        void AddIntersection(Vector3 position){
            int intId = wps.intersections.Count;
            GameObject intGo = new GameObject("Intersection-" + intId);
            intGo.transform.position = position;
            intGo.transform.SetParent(wps.transform.GetChild(1).transform);
            BoxCollider bc = intGo.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            Intersection intersection = intGo.AddComponent<Intersection>();
            intersection.id = intId;
            wps.intersections.Add(intersection);
        }

        void RestructureSystem(){

            //Rename and restructure segments and waypoitns
            List<Segment> nSegments = new List<Segment>();
            int itSeg = 0;
            foreach(Segment segment in wps.segments){
                if(segment != null){
                    List<Waypoint> nWaypoints = new List<Waypoint>();
                    segment.id = itSeg;
                    segment.gameObject.name = "Segment-" + itSeg;
                    
                    int itWp = 0;
                    foreach(Waypoint waypoint in segment.waypoints){
                        if(waypoint != null) {
                            waypoint.Refresh(itWp, segment);
                            nWaypoints.Add(waypoint);
                            itWp++;
                        }
                    }

                    segment.waypoints = nWaypoints;
                    nSegments.Add(segment);
                    itSeg++;
                }
            }

            //Check if next segments still exist
            foreach(Segment segment in nSegments){
                List<Segment> nNextSegments = new List<Segment>();
                foreach(Segment nextSeg in segment.nextSegments){
                    if(nextSeg != null){
                        nNextSegments.Add(nextSeg);
                    }
                }
                segment.nextSegments = nNextSegments;
            }
            wps.segments = nSegments;

            //Check intersections
            List<Intersection> nIntersections = new List<Intersection>();
            int itInter = 0;
            foreach(Intersection intersection in wps.intersections){
                if(intersection != null){
                    intersection.id = itInter;
                    intersection.gameObject.name = "Intersection-" + itInter;
                    nIntersections.Add(intersection);
                    itInter++;
                }
            }
            wps.intersections = nIntersections;
            
            //Tell Unity that something changed and the scene has to be saved
            if (!EditorUtility.IsDirty(target)) {
                EditorUtility.SetDirty(target);
            }

            Debug.Log("[Traffic Simulation] Successfully rebuilt the traffic system.");
        }
    }
}
