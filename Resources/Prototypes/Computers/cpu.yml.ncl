let
  { MiB, KiB } = import "~/Prototypes/units.ncl"
in
let
  { EvenNumber, .. } = import "~/Prototypes/contracts.ncl"
in
let Cpu = {
    type
      | String,
    id
      | String,
    name
      | String,
    memory
      | EvenNumber,
    flashMemory
      | EvenNumber,
    ipq
      | Number,
    drawRate
      | Number
  }
in
[
  ## Nano
  {
    type = "cpu",
    id = "VirtTechNanoV",
    name = "VirtTech Nano-V",
    memory = KiB 512,
    flashMemory = KiB 1,
    ipq = 10,
    drawRate = 0.2
  },
  {
    type = "cpu",
    id = "VirtTechNanoVEX",
    name = "VirtTech Nano-V EX",
    memory = MiB 1,
    flashMemory = KiB 1,
    ipq = 20,
    drawRate = 0.3
  },
  ## Micro
  {
    type = "cpu",
    id = "VirtTechMicroV",
    name = "VirtTech Micro-V",
    memory = MiB 1,
    flashMemory = KiB 4,
    ipq = 50,
    drawRate = 0.5
  },
  {
    type = "cpu",
    id = "VirtTechMicroVEX",
    name = "VirtTech Micro-V EX",
    memory = MiB 3,
    flashMemory = KiB 4,
    ipq = 60,
    drawRate = 0.6
  },
  {
    type = "cpu",
    id = "VirtTechMicroVEEX",
    name = "VirtTech Micro-V ExtraEX",
    memory = MiB 6,
    flashMemory = KiB 4,
    ipq = 70,
    drawRate = 0.7
  },
  ## Mini
  {
    type = "cpu",
    id = "VirtTechMiniV",
    name = "VirtTech Mini-V",
    memory = MiB 1,
    flashMemory = KiB 8,
    ipq = 100,
    drawRate = 0.8
  },
  {
    type = "cpu",
    id = "VirtTechMiniVEX",
    name = "VirtTech Mini-V EX",
    memory = MiB 3,
    flashMemory = KiB 8,
    ipq = 200,
    drawRate = 0.9
  },
  {
    type = "cpu",
    id = "VirtTechMiniVEEX",
    name = "VirtTech Mini-V ExtraEX",
    memory = MiB 6,
    flashMemory = KiB 8,
    ipq = 300,
    drawRate = 1.0
  },
  ## Pro
  {
    type = "cpu",
    id = "VirtTechProV",
    name = "VirtTech Pro-V",
    memory = MiB 3,
    flashMemory = KiB 12,
    ipq = 500,
    drawRate = 1.5
  },
  {
    type = "cpu",
    id = "VirtTechProVEX",
    name = "VirtTech Pro-V EX",
    memory = MiB 6,
    flashMemory = KiB 12,
    ipq = 600,
    drawRate = 1.6
  },
  {
    type = "cpu",
    id = "VirtTechProVEEX",
    name = "VirtTech Pro-V ExtraEX",
    memory = MiB 8,
    flashMemory = KiB 12,
    ipq = 700,
    drawRate = 1.7
  },
  ## Super
  {
    type = "cpu",
    id = "VirtTechSuperV",
    name = "VirtTech Super-V",
    memory = MiB 6,
    flashMemory = KiB 16,
    ipq = 1000,
    drawRate = 2.0
  },
  {
    type = "cpu",
    id = "VirtTechSuperVEX",
    name = "VirtTech Super-V EX",
    memory = MiB 8,
    flashMemory = KiB 16,
    ipq = 1500,
    drawRate = 2.5
  },
  {
    type = "cpu",
    id = "VirtTechSuperVEEX",
    name = "VirtTech Super-V ExtraEX",
    memory = MiB 12,
    flashMemory = KiB 16,
    ipq = 2000,
    drawRate = 3.0
  }
] | Array Cpu
