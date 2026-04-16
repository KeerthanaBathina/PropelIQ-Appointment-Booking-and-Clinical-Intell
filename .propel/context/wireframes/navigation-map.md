# Navigation Map - Unified Patient Access & Clinical Intelligence Platform

## Overview

Cross-screen navigation index derived from FL-001 through FL-010 prototype flows defined in figma_spec.md Section 11. Each entry maps source screen elements to target screens with the triggering user action.

## Global Navigation

| Element | Location | Action | Target Screen | Available To |
|---------|----------|--------|---------------|-------------|
| Logo | Header (left) | Click | SCR-002 Dashboard Router | All |
| Sidebar: Dashboard | Sidebar | Click | SCR-005 / SCR-010 / SCR-015 (role-based) | All |
| Sidebar: Appointments | Sidebar | Click | SCR-006 Appointment Booking | Patient |
| Sidebar: History | Sidebar | Click | SCR-007 Appointment History | Patient |
| Sidebar: Intake | Sidebar | Click | SCR-008 AI Intake | Patient |
| Sidebar: Queue | Sidebar | Click | SCR-011 Arrival Queue | Staff |
| Sidebar: Documents | Sidebar | Click | SCR-012 Document Upload | Staff |
| Sidebar: Patients | Sidebar | Click | SCR-016 Patient Search | Staff, Admin |
| Sidebar: Configuration | Sidebar | Click | SCR-015 Admin Dashboard | Admin |
| User Menu: Logout | Header (right) | Click | SCR-001 Login | All |

## Screen-Level Navigation

### SCR-001 Login

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #login-btn | Login Button | Click (valid credentials) | SCR-002 Dashboard Router | FL-001 |
| #forgot-pwd | Forgot Password Link | Click | SCR-004 Password Reset | FL-001 |
| #create-account | Create Account Link | Click | SCR-003 Registration | FL-002 |

### SCR-002 Dashboard Router

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #patient-dash | Patient Role Card | Auto-redirect | SCR-005 Patient Dashboard | FL-001 |
| #staff-dash | Staff Role Card | Auto-redirect | SCR-010 Staff Dashboard | FL-001 |
| #admin-dash | Admin Role Card | Auto-redirect | SCR-015 Admin Dashboard | FL-001 |

### SCR-003 Registration

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #register-btn | Register Button | Click (valid) | SCR-003 Success State | FL-002 |
| #back-login | Back to Login Link | Click | SCR-001 Login | FL-002 |

### SCR-004 Password Reset

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #reset-btn | Send Reset Link Button | Click | SCR-004 Success State | FL-001 |
| #back-login | Back to Login Link | Click | SCR-001 Login | FL-001 |

### SCR-005 Patient Dashboard

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #book-apt | Book Appointment Button | Click | SCR-006 Appointment Booking | FL-003 |
| #complete-intake | Complete Intake Button | Click | SCR-008 AI Intake | FL-004 |
| #view-history | View History Link | Click | SCR-007 Appointment History | - |
| #cancel-apt | Cancel Appointment Button | Click | Cancel Appointment Dialog | FL-003 |
| #notif-prefs | Notification Preferences | Click | Notification Drawer | - |

### SCR-006 Appointment Booking

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #confirm-booking | Confirm Booking Button | Click | Booking Confirmation Modal | FL-003 |
| #modal-confirm | Modal Confirm Button | Click | SCR-005 Patient Dashboard (toast) | FL-003 |
| #modal-conflict | Slot Conflict Modal | Displayed on 409 | SCR-006 (alternative slots) | FL-003 |
| #breadcrumb-dash | Breadcrumb: Dashboard | Click | SCR-005 Patient Dashboard | - |

### SCR-007 Appointment History

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #cancel-past | Cancel Appointment | Click | Cancel Appointment Dialog | - |
| #breadcrumb-dash | Breadcrumb: Dashboard | Click | SCR-005 Patient Dashboard | - |

### SCR-008 AI Conversational Intake

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #switch-manual | Switch to Manual Form | Click | SCR-009 Manual Intake | FL-004 |
| #confirm-intake | Confirm Intake Button | Click | SCR-005 Patient Dashboard (badge) | FL-004 |
| #breadcrumb-dash | Breadcrumb: Dashboard | Click | SCR-005 Patient Dashboard | - |

### SCR-009 Manual Intake Form

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #submit-intake | Submit Intake Button | Click (valid) | SCR-005 Patient Dashboard (badge) | FL-004 |
| #back-ai | Back to AI Intake | Click | SCR-008 AI Intake | FL-004 |
| #breadcrumb-dash | Breadcrumb: Dashboard | Click | SCR-005 Patient Dashboard | - |

### SCR-010 Staff Dashboard

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #walkin-btn | Walk-in Registration | Click | Walk-in Registration Modal | FL-005 |
| #walkin-modal-book | Modal: Book Walk-in | Click | SCR-006 Appointment Booking (staff mode) | FL-005 |
| #view-queue | View Queue Link | Click | SCR-011 Arrival Queue | FL-006 |
| #search-patient | Patient Search Bar | Submit | SCR-016 Patient Search | - |
| #patient-row | Patient Row Click | Click | SCR-013 Patient Profile 360 | FL-007 |

### SCR-011 Arrival Queue Dashboard

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #mark-arrived | Mark Arrived Button | Click | SCR-011 (updated row) | FL-006 |
| #set-urgent | Set Urgent Button | Click | SCR-011 (reordered) | FL-006 |
| #mark-in-visit | Mark In-Visit Button | Click | SCR-011 (updated status) | FL-006 |
| #patient-name | Patient Name Link | Click | SCR-013 Patient Profile 360 | - |
| #breadcrumb-staff | Breadcrumb: Staff Dashboard | Click | SCR-010 Staff Dashboard | - |

### SCR-012 Document Upload & Parsing

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #upload-zone | Drop Zone / File Picker | Drop/Click | SCR-012 (processing state) | FL-007 |
| #view-profile | Back to Profile | Click | SCR-013 Patient Profile 360 | FL-007 |
| #breadcrumb-profile | Breadcrumb: Patient Profile | Click | SCR-013 Patient Profile 360 | - |

### SCR-013 Patient Profile 360

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #upload-tab | Upload Documents Tab | Click | SCR-012 Document Upload | FL-007 |
| #coding-link | View Medical Codes | Click | SCR-014 Medical Coding | FL-008 |
| #conflict-row | Flagged Conflict Row | Click | Conflict Resolution Modal | FL-007 |
| #breadcrumb-staff | Breadcrumb: Staff Dashboard | Click | SCR-010 Staff Dashboard | - |

### SCR-014 Medical Coding Review

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #approve-all | Approve All Codes | Click | SCR-014 (updated rates) | FL-008 |
| #override-code | Override Code Button | Click | Code Override Modal | FL-008 |
| #breadcrumb-profile | Breadcrumb: Patient Profile | Click | SCR-013 Patient Profile 360 | - |
| #breadcrumb-staff | Breadcrumb: Staff Dashboard | Click | SCR-010 Staff Dashboard | - |

### SCR-015 Admin Configuration Dashboard

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #save-config | Save Configuration | Click (valid) | SCR-015 (success toast) | FL-009 |
| #user-deactivate | Deactivate User | Click | Delete User Confirmation Dialog | FL-009 |
| #search-patient | Patient Search Bar | Submit | SCR-016 Patient Search | - |

### SCR-016 Patient Search Results

| Element ID | Element | Action | Target Screen | Flow Reference |
|-----------|---------|--------|---------------|----------------|
| #patient-row | Patient Row Click | Click | SCR-013 Patient Profile 360 | - |
| #breadcrumb-staff | Breadcrumb: Staff Dashboard | Click | SCR-010 Staff Dashboard | - |
| #breadcrumb-admin | Breadcrumb: Admin Dashboard | Click | SCR-015 Admin Dashboard | - |

## Dead-End Analysis

| Screen | Outbound Links | Status |
|--------|---------------|--------|
| SCR-001 Login | 3 (Dashboard, Registration, Password Reset) | OK |
| SCR-002 Dashboard Router | 3 (auto-redirects to role dashboards) | OK |
| SCR-003 Registration | 2 (Success state, Back to Login) | OK - Success is intentional exit |
| SCR-004 Password Reset | 2 (Success state, Back to Login) | OK - Success is intentional exit |
| SCR-005 Patient Dashboard | 4+ (Book, Intake, History, Cancel) | OK |
| SCR-006 Appointment Booking | 3 (Confirm, Conflict, Breadcrumb) | OK |
| SCR-007 Appointment History | 2 (Cancel, Breadcrumb) | OK |
| SCR-008 AI Intake | 3 (Manual Switch, Confirm, Breadcrumb) | OK |
| SCR-009 Manual Intake | 3 (Submit, Back to AI, Breadcrumb) | OK |
| SCR-010 Staff Dashboard | 4+ (Walk-in, Queue, Search, Patient) | OK |
| SCR-011 Arrival Queue | 4+ (Mark actions, Patient link, Breadcrumb) | OK |
| SCR-012 Document Upload | 2 (Upload action, Back to Profile) | OK |
| SCR-013 Patient Profile 360 | 3+ (Upload, Coding, Conflicts, Breadcrumb) | OK |
| SCR-014 Medical Coding | 3 (Approve, Override, Breadcrumb) | OK |
| SCR-015 Admin Dashboard | 3 (Save, Deactivate, Search) | OK |
| SCR-016 Patient Search | 2 (Patient row, Breadcrumb) | OK |

**Dead-End Screens**: None. All screens have at least 2 outbound navigation paths (including breadcrumb/sidebar).

## Flow Completeness Summary

| Flow ID | Flow Name | Screens Involved | All Screens Wired | Status |
|---------|-----------|-----------------|-------------------|--------|
| FL-001 | Patient Authentication | SCR-001, SCR-002, SCR-004 | Yes | Complete |
| FL-002 | Patient Registration | SCR-001, SCR-003 | Yes | Complete |
| FL-003 | Patient Books Appointment | SCR-005, SCR-006 | Yes | Complete |
| FL-004 | Patient Completes AI Intake | SCR-005, SCR-008, SCR-009 | Yes | Complete |
| FL-005 | Staff Registers Walk-in | SCR-010, SCR-006, SCR-011 | Yes | Complete |
| FL-006 | Staff Manages Arrival Queue | SCR-011 | Yes | Complete |
| FL-007 | Staff Uploads & Reviews Documents | SCR-012, SCR-013 | Yes | Complete |
| FL-008 | Medical Coding Review | SCR-014 | Yes | Complete |
| FL-009 | Admin Configures System | SCR-015 | Yes | Complete |
| FL-010 | Error Recovery | All screens | Yes | Complete |
