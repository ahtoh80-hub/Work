UPDATE OSBL_SCS_ТЕХНОЛ_ОБЪЕКТЫ INNER JOIN OSBL_IO_List_SCS_v5 ON OSBL_SCS_ТЕХНОЛ_ОБЪЕКТЫ.Loop = OSBL_IO_List_SCS_v5.Loop SET OSBL_SCS_ТЕХНОЛ_ОБЪЕКТЫ.Доп_параметр = [OSBL_IO_List_SCS_v5].[Main_module] & " / " & [OSBL_IO_List_SCS_v5].[Redundant_module]
WHERE (((OSBL_IO_List_SCS_v5.Main_module) Is Not Null));
