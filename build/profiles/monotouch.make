# -*- makefile -*-

PROFILE_LIB_DIR = ../../../mono/mcs/class/lib/$(PROFILE)/

profile-check:
	@if ! test -d $(PROFILE_LIB_DIR); then echo The directory $(PROFILE_LIB_DIR) does not exist; exit 1; fi

PROFILE_VBNC_FLAGS = /sdkpath:$(PROFILE_LIB_DIR)

NO_INSTALL=yes